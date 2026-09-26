using NetOpenGrid.Domain.Columns;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using NetOpenGrid.Domain.GridQuerying;

namespace NetOpenGrid.Persistence.Npgsql;

/// <summary>
/// Translates a <see cref="GridQuery"/> into SQL fragments plus parameters.
/// Pure: no connection, no I/O, fully unit-testable — which is the point, because this
/// is the only place user-controlled strings meet SQL.
/// </summary>
public static class GridSqlBuilder
{
    /// <summary>
    /// Upper bound on <see cref="GridQuery.Search"/> before it is wrapped in <c>%</c> and ORed
    /// across every searchable column. A leading <c>%</c> already defeats any index on these
    /// columns (sequential scan), so an uncapped pathological string is a cheap CPU sink —
    /// capped rather than rejected outright, since search is best-effort discovery, not a
    /// strict field contract, and failing the whole grid over an over-long paste is worse than
    /// silently truncating it.
    /// </summary>
    private const int MaxSearchLength = 200;

    public static (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildWhere(
        GridSqlMap map, GridQuery query, string? excludeField)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(query);

        // Parenthesized: BaseWhere is caller-supplied SQL text that may itself contain a
        // top-level OR ("A OR B"). Without the parens, "AND" (which binds tighter than "OR")
        // silently defeats half of it: "WHERE A OR B AND filter" parses as "A OR (B AND
        // filter)", leaving the "A" branch completely unfiltered. GridSqlMap's own constructor
        // already rejects an empty/whitespace BaseWhere, so this is never "WHERE ()".
        var sb = new StringBuilder("WHERE (").Append(map.BaseWhere).Append(')');
        var parameters = new List<NpgsqlParameter>();

        foreach (var filter in query.Filters)
        {
            if (excludeField is not null && string.Equals(filter.Field, excludeField, StringComparison.Ordinal))
            {
                continue;
            }

            if (!map.Columns.TryGetValue(filter.Field, out var column))
            {
                // Constant message + the field name only as a structured Errors key, never in
                // free text: ArgumentException-family .Message flows straight into
                // ErrorHandlingMiddleware's 400 JSON body, and bulk-import-resolve-catalogs.js
                // (among other callers) splices `data.message` into innerHTML unescaped.
                // Echoing attacker-controlled text there is a reflected-XSS sink, not merely an
                // information-disclosure oracle. The Errors dictionary is instead the
                // CLAUDE.md-mandated field-level shape, consumed elsewhere via `.textContent`
                // (safe, auto-escaped) rather than innerHTML.
                throw new GridQueryException(new Dictionary<string, string[]>
                {
                    [filter.Field] = ["Unknown grid field."],
                });
            }

            sb.Append(" AND ").Append(AppendCondition(column, filter, parameters));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var searchable = map.Columns.Values.Where(static c => c.Searchable).ToArray();
            if (searchable.Length > 0)
            {
                var term = query.Search.Trim();
                if (term.Length > MaxSearchLength)
                {
                    term = term[..MaxSearchLength];
                }

                var name = Bind(parameters, $"%{EscapeLikePattern(term)}%");
                sb.Append(" AND (")
                  .Append(string.Join(" OR ", searchable.Select(c => $"{TextExpression(c)} ILIKE {name}")))
                  .Append(')');
            }
        }

        return (sb.ToString(), parameters);
    }

    /// <summary>
    /// Builds one filter's SQL fragment and binds its value(s).
    ///
    /// <para><b>Scope of the client-input error handling below:</b> only the two narrow
    /// <c>try/catch</c> blocks in this method (the <c>In</c> JSON-array parse, and the scalar
    /// <c>Parse</c> call) convert a failure into <see cref="GridQueryException"/> — deliberately
    /// scoped to exactly the two known parse-failure paths, never a blanket wrap of this whole
    /// method or of <see cref="BuildWhere"/>. A bug in this file (a <c>NullReferenceException</c>,
    /// say) must still surface as an unhandled exception into <c>ErrorHandlingMiddleware</c>'s
    /// 500 arm with its ERROR-level Serilog entry — misreporting an internal bug as "the client
    /// sent bad input" would hide it from the one channel built to catch it.</para>
    /// </summary>
    private static string AppendCondition(GridSqlColumn column, FilterDescriptor filter, List<NpgsqlParameter> parameters)
    {
        var col = column.SqlExpression;

        if (filter.Operator is FilterOperator.IsEmpty)
        {
            return $"({col} IS NULL OR {TextExpression(column)} = '')";
        }

        if (filter.Operator is FilterOperator.IsNotEmpty)
        {
            return $"({col} IS NOT NULL AND {TextExpression(column)} <> '')";
        }

        if (filter.Operator is FilterOperator.In)
        {
            var target = Nullable.GetUnderlyingType(column.ClrType) ?? column.ClrType;
            object[] elements;
            try
            {
                elements = ParseInPayload(filter.Value).Select(v => Parse(v, column.ClrType)).ToArray();
            }
            catch (Exception ex) when (ex is JsonException or FormatException or NotSupportedException)
            {
                // Client-input shape/format problem (malformed JSON payload, or an element that
                // doesn't parse to the column's CLR type) — a 400 with a field-level error, not
                // an unhandled-exception 500. See InvalidFilterValue.
                throw InvalidFilterValue(filter.Field);
            }

            var typed = ToTypedArray(elements, target);
            var arrayName = BindArray(parameters, typed, ElementDbType(target));
            return $"{col} = ANY({arrayName})";
        }

        object value;
        try
        {
            value = filter.Operator switch
            {
                FilterOperator.Contains => $"%{EscapeLikePattern(filter.Value)}%",
                FilterOperator.StartsWith => $"{EscapeLikePattern(filter.Value)}%",
                FilterOperator.EndsWith => $"%{EscapeLikePattern(filter.Value)}",
                _ => Parse(filter.Value, column.ClrType),
            };
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException)
        {
            // Same client-input reasoning as the `In` branch above: EscapeLikePattern never
            // throws (plain string.Replace), so the only thing this can be catching is Parse's
            // FormatException ("not-a-number" against a decimal column) or NotSupportedException
            // (an unmapped column CLR type) — never an unrelated internal bug.
            throw InvalidFilterValue(filter.Field);
        }

        var name = Bind(parameters, value);

        return filter.Operator switch
        {
            FilterOperator.Equals => $"{col} = {name}",
            FilterOperator.NotEquals => $"{col} <> {name}",
            FilterOperator.Contains or FilterOperator.StartsWith or FilterOperator.EndsWith => $"{TextExpression(column)} ILIKE {name}",
            FilterOperator.GreaterThan => $"{col} > {name}",
            FilterOperator.GreaterThanOrEqual => $"{col} >= {name}",
            FilterOperator.LessThan => $"{col} < {name}",
            FilterOperator.LessThanOrEqual => $"{col} <= {name}",
            _ => throw new ArgumentOutOfRangeException(nameof(filter), $"Unsupported operator {filter.Operator}."),
        };
    }

    /// <summary>
    /// A client-input shape/format problem on one field: a malformed <c>In</c> JSON payload, or
    /// a value that doesn't parse to the column's declared CLR type (e.g. "not-a-number" against
    /// a decimal column). Same constant-message + field-as-key pattern as the unknown-field
    /// throw above, for the same reason — <see cref="GridQueryException"/>'s message is the
    /// fixed literal "Validation failed." regardless of input, so nothing user-controlled ever
    /// reaches the free-text <c>Message</c> that <c>ErrorHandlingMiddleware</c> puts in the 400
    /// body and that at least one caller (<c>bulk-import-resolve-catalogs.js</c>) splices into
    /// innerHTML. Before this fix, both <c>Parse</c>'s <see cref="FormatException"/> and
    /// <c>In</c>'s <see cref="JsonException"/> fell through to <c>ErrorHandlingMiddleware</c>'s
    /// default arm — an unhandled-exception 500 with an ERROR-level Serilog entry — for what is,
    /// by definition, a client-input problem, not an internal bug.
    /// </summary>
    private static GridQueryException InvalidFilterValue(string field) =>
        new(new Dictionary<string, string[]> { [field] = ["Invalid filter value."] });

    /// <summary>
    /// Binds one value as the next positional parameter and returns its name. Computing the
    /// name (<c>@p{parameters.Count}</c>) and appending to <paramref name="parameters"/> happen
    /// atomically in this one call, so the "every @pN is unique" invariant holds by
    /// construction instead of by convention. Two independent call sites each computing
    /// <c>$"@p{parameters.Count}"</c> before either one calls <c>Add</c> — which is what this
    /// file did before this fix — is exactly how a future two-value operator (a <c>Between</c>,
    /// say, or an <c>ESCAPE</c> parameter) would silently emit the same name twice.
    /// </summary>
    private static string Bind(List<NpgsqlParameter> parameters, object value)
    {
        var name = $"@p{parameters.Count}";
        parameters.Add(new NpgsqlParameter(name, value));
        return name;
    }

    /// <summary>Same "compute name and add atomically" invariant as <see cref="Bind"/>, for a typed array bound with an explicit element <see cref="NpgsqlDbType"/> (see <see cref="ElementDbType"/>).</summary>
    private static string BindArray(List<NpgsqlParameter> parameters, Array value, NpgsqlDbType elementType)
    {
        var name = $"@p{parameters.Count}";
        parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Array | elementType) { Value = value });
        return name;
    }

    /// <summary>
    /// The <c>In</c> operator's payload is a JSON string array — NOT <c>'|'</c>-delimited.
    /// NetOpenGrid's own request parser (<c>GridRequestParser.IsValidInPayload</c>) already
    /// enforces "non-empty JSON string array" before a <see cref="FilterDescriptor"/> with
    /// <see cref="FilterOperator.In"/> is ever created for a request that went through it;
    /// <c>'|'</c> is that library's unrelated group-path delimiter, and splitting on it here
    /// silently produced one wrong element instead of the real list. <see cref="GridSqlBuilder"/>
    /// is a public API a caller could invoke directly, bypassing that parser, so this validates
    /// the shape itself rather than trusting it.
    ///
    /// <para><b>Malformed payload:</b> throws <see cref="JsonException"/> — invalid JSON, valid
    /// JSON that isn't an array of strings, or a JSON <c>null</c> — rather than silently
    /// no-op'ing. Silently producing zero matching rows on bad input is exactly the failure
    /// mode this fix replaces (a user ticking two checkboxes in a filter dropdown getting a
    /// silently empty grid, with no error anywhere). Fail loud, not quiet. <see cref="AppendCondition"/>
    /// catches this <see cref="JsonException"/> and converts it to a field-level
    /// <see cref="GridQueryException"/> (400), so it never reaches a caller as an unhandled
    /// exception — see <see cref="InvalidFilterValue"/>.</para>
    ///
    /// <para><b>An empty payload (<c>[]</c>) is NOT malformed:</b> a syntactically valid empty
    /// JSON array binds a zero-length typed array and resolves to <c>= ANY('{}')</c>, which
    /// correctly matches no rows — the well-defined SQL semantics an empty <c>In</c> selection
    /// should have, and the reason <c>= ANY(array)</c> was kept over <c>IN (@p0, @p1, ...)</c>
    /// in the first place.</para>
    /// </summary>
    private static List<string> ParseInPayload(string? raw)
    {
        var values = JsonSerializer.Deserialize<List<string>>(raw ?? "null");
        return values ?? throw new JsonException(
            "'In' filter value must be a JSON array of strings (this is NOT the '|'-delimited "
            + "format used elsewhere for group paths), and not a JSON null.");
    }

    /// <summary>
    /// Copies boxed <paramref name="values"/> (as produced by <see cref="Parse"/>, each
    /// actually boxing a <paramref name="target"/>) into a genuinely-typed array (<c>decimal[]</c>,
    /// <c>string[]</c>, <c>Guid[]</c>, ...). An <c>object[]</c> has no Postgres array type
    /// Npgsql can infer — and an empty <c>object[]</c> has nothing to infer from at all — so
    /// binding the boxed array directly throws or silently mis-binds. Works correctly for a
    /// zero-length input too, since the array's element type is fixed by
    /// <see cref="Array.CreateInstance(Type, int)"/> regardless of its length.
    /// </summary>
    private static Array ToTypedArray(object[] values, Type target)
    {
        var typed = Array.CreateInstance(target, values.Length);
        Array.Copy(values, typed, values.Length);
        return typed;
    }

    /// <summary>
    /// The Postgres array element type for a column's CLR type, so the <c>In</c> parameter's
    /// <see cref="NpgsqlDbType"/> is set explicitly rather than left entirely to inference — the
    /// same belt-and-suspenders precedent already in this codebase at
    /// <c>InventoryAdjustmentImportDefinition.LoadProductsByCodeAsync</c>, which sets
    /// <c>NpgsqlDbType.Array | NpgsqlDbType.Text</c> explicitly even for an
    /// already-strongly-typed <c>string[]</c>.
    /// </summary>
    private static NpgsqlDbType ElementDbType(Type target)
    {
        if (target == typeof(string)) return NpgsqlDbType.Text;
        if (target == typeof(Guid)) return NpgsqlDbType.Uuid;
        if (target == typeof(bool)) return NpgsqlDbType.Boolean;
        if (target == typeof(int)) return NpgsqlDbType.Integer;
        if (target == typeof(long)) return NpgsqlDbType.Bigint;
        if (target == typeof(decimal)) return NpgsqlDbType.Numeric;
        if (target == typeof(double)) return NpgsqlDbType.Double;
        if (target == typeof(DateOnly)) return NpgsqlDbType.Date;
        if (target == typeof(DateTime)) return NpgsqlDbType.TimestampTz;

        throw new NotSupportedException($"No 'In' array mapping for column type {target.Name}.");
    }

    /// <summary>
    /// Escapes LIKE/ILIKE metacharacters (<c>\</c>, <c>%</c>, <c>_</c>) in a raw user value
    /// before it is wrapped in the <c>%</c> wildcard delimiters for
    /// <c>Contains</c>/<c>StartsWith</c>/<c>EndsWith</c>/global search. Unescaped, user text is
    /// interpreted as a LIKE *pattern* rather than literal text — <c>Contains "50%"</c> would
    /// match "50 followed by anything", <c>Contains "a_c"</c> would match "abc" — and a value
    /// ending in an unescaped <c>\</c> makes Postgres reject the whole query with SQLSTATE 22025
    /// ("LIKE pattern must not end with escape character"). Postgres's default LIKE escape
    /// character is <c>\</c>, so no explicit <c>ESCAPE</c> clause is needed once this has run.
    /// Backslash is escaped first so escaping <c>%</c>/<c>_</c> afterwards doesn't get
    /// re-escaped.
    /// </summary>
    private static string EscapeLikePattern(string? value) =>
        (value ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");

    /// <summary>
    /// The column expression to use on the text (string) side of a comparison — <c>ILIKE</c>
    /// or an empty-string check. String columns are used bare (<c>p.company_name</c>) so
    /// PostgreSQL can still use a btree/trigram index on them; every other CLR type gets an
    /// explicit <c>::text</c> cast (<c>p.credit_limit::text</c>), because <c>ILIKE</c>/<c>= ''</c>
    /// against a non-text column (numeric, uuid, timestamp, ...) raises
    /// <c>operator does not exist</c> and 500s the request.
    /// </summary>
    private static string TextExpression(GridSqlColumn column)
    {
        var target = Nullable.GetUnderlyingType(column.ClrType) ?? column.ClrType;
        return target == typeof(string) ? column.SqlExpression : $"{column.SqlExpression}::text";
    }

    /// <summary>
    /// Parses a raw query-string value to the column's CLR type. Invariant culture throughout —
    /// a decimal arrives as "1500.50" regardless of the browser's locale.
    /// </summary>
    private static object Parse(string? raw, Type clrType)
    {
        var text = raw ?? string.Empty;
        var target = Nullable.GetUnderlyingType(clrType) ?? clrType;

        if (target == typeof(string)) return text;
        if (target == typeof(Guid)) return Guid.Parse(text);
        if (target == typeof(bool)) return bool.Parse(text);
        if (target == typeof(int)) return int.Parse(text, CultureInfo.InvariantCulture);
        if (target == typeof(long)) return long.Parse(text, CultureInfo.InvariantCulture);
        if (target == typeof(decimal)) return decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture);
        if (target == typeof(double)) return double.Parse(text, CultureInfo.InvariantCulture);
        if (target == typeof(DateOnly)) return DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        // AssumeUniversal is required, not just AdjustToUniversal: a string with no offset
        // ("2026-01-15 10:30") parses with Kind=Unspecified under AdjustToUniversal alone,
        // and Npgsql refuses to write Kind=Unspecified to a `timestamptz` column (328 of them
        // in this schema). AssumeUniversal + AdjustToUniversal together guarantee Kind=Utc
        // regardless of whether the input string carries an explicit offset.
        if (target == typeof(DateTime)) return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        throw new NotSupportedException($"No parser for column type {target.Name}.");
    }

    /// <summary>
    /// One <c>SELECT SUM/AVG/MIN/MAX</c> over the filtered projection, wrapped as a derived table
    /// exactly like the COUNT query (the WHERE stays inside the parens, next to the projection's
    /// own aliases). Every result is cast to <c>numeric</c> so the reader can always take a decimal.
    /// Fields missing from the map are skipped; <paramref name="where"/> is the output of
    /// <see cref="BuildWhere"/>, so the only identifiers composed here come from the map itself.
    /// </summary>
    public static (string Sql, IReadOnlyList<(string Field, GridAggregate Function)> Slots) BuildAggregateSelect(
        GridSqlMap map, string where, IEnumerable<(string Field, GridAggregate Functions)> columns)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(where);
        ArgumentNullException.ThrowIfNull(columns);

        var selects = new List<string>();
        var slots = new List<(string, GridAggregate)>();

        foreach (var (field, functions) in columns)
        {
            if (!map.Columns.TryGetValue(field, out var column)) continue;

            foreach (var function in functions.Functions())
            {
                var sqlFunction = function switch
                {
                    GridAggregate.Sum => "SUM",
                    GridAggregate.Avg => "AVG",
                    GridAggregate.Min => "MIN",
                    _ => "MAX"
                };

                selects.Add($@"{sqlFunction}(src.""{column.OutputAlias}"")::numeric");
                slots.Add((field, function));
            }
        }

        if (selects.Count == 0)
        {
            return (string.Empty, slots);
        }

        return ($"SELECT {string.Join(", ", selects)} FROM ({map.Projection} {where}) AS src", slots);
    }

    public static string BuildOrderBy(GridSqlMap map, GridQuery query)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(query);

        var parts = new List<string>();
        foreach (var sort in query.Sorts)
        {
            if (!map.Columns.TryGetValue(sort.Field, out var column)) continue;   // unknown sorts are dropped, never composed
            parts.Add(sort.Direction == SortDirection.Descending
                ? $"{column.SqlExpression} DESC"
                : column.SqlExpression);
        }

        parts.Add(map.DefaultOrderBy);
        return "ORDER BY " + string.Join(", ", parts);
    }
}
