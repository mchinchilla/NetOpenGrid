using System.Globalization;
using System.Reflection;
using Npgsql;
using NetOpenGrid.Application.Engine;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain.Abstractions;
using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Domain.GridQuerying;
using NetOpenGrid.Domain.Results;

namespace NetOpenGrid.Persistence.Npgsql;

/// <summary>
/// SQL push-down data source for NetOpenGrid over Npgsql. It uses no RepoDb API at all — only raw
/// <see cref="NpgsqlCommand"/> against connections from the singleton
/// <see cref="NpgsqlDataSource"/>; see "Parameter binding" and "Row mapping" below for why neither
/// RepoDb's parameter path nor its reader→entity bridge is usable here. (This class was called
/// <c>RepoDbGridDataSource</c> until the whole-branch review: the name promised a dependency it
/// never had.)
///
/// <para><b>Connection lifetime.</b> NetOpenGrid registers <c>IGridRuntime</c> as a keyed
/// SINGLETON, so this object outlives every request and must never touch the scoped
/// <c>IDbConnection</c>. It opens a fresh connection from the singleton
/// <see cref="NpgsqlDataSource"/> per call — the pattern documented in
/// <c>docs/connection-lifetime.md</c>. Npgsql has no MARS, so awaits are sequential.</para>
///
/// <para><b>Parameter binding.</b> <see cref="GridSqlBuilder.BuildWhere"/> binds <c>In</c>
/// filters as a typed array parameter with an explicit <see cref="NpgsqlTypes.NpgsqlDbType"/>
/// (<c>NpgsqlDbType.Array | elementType</c>), because Npgsql cannot infer a PG array type
/// from a boxed <c>object[]</c> — and an empty array has nothing to infer from at all.
/// Flattening those <see cref="NpgsqlParameter"/> instances into a
/// <c>Dictionary&lt;string, object?&gt;</c> (as an earlier draft of this class did) throws the
/// <c>NpgsqlDbType</c> away and silently breaks the <c>In</c> operator again. This class
/// therefore builds every command with raw <see cref="NpgsqlCommand"/> and adds the actual
/// <see cref="NpgsqlParameter"/> objects the builder produced — the same pattern already used
/// in this codebase wherever an explicit <c>NpgsqlDbType</c> matters (e.g.
/// <c>InventoryAdjustmentImportDefinition.LoadProductsByCodeAsync</c>,
/// <c>StripeWebhookEventRepository</c>). A single <see cref="NpgsqlParameter"/> instance can
/// only belong to one <see cref="NpgsqlParameterCollection"/> at a time — and the ownership guard
/// fires even after the owning command has been disposed — and one call here can issue two or
/// three commands against the same WHERE (a COUNT, sometimes a COUNT DISTINCT, then a page
/// SELECT/GROUP BY), so <see cref="CreateCommand"/> adds a <see cref="NpgsqlParameter.Clone"/> of
/// each parameter to every command, never the original builder-owned instance.</para>
///
/// <para><b>Row mapping.</b> Rows are materialized from the live <see cref="NpgsqlDataReader"/>
/// by <see cref="MapRowsAsync{TEntity}"/>, which matches each column name to a writable public
/// property of the same name (case-insensitive) — <typeparamref name="TRow"/> must therefore
/// carry NO <c>[Map]</c> attributes and its property names must equal the SQL <c>AS</c> aliases
/// exactly. RepoDb's own reader→entity bridge
/// (<c>RepoDb.Extensions.DataReaderExtension.AsEnumerable&lt;TEntity&gt;</c>) does the same
/// by-name match but ships <c>[Obsolete("This extended method will be removed soon.")]</c> in
/// the pinned RepoDb version, so this reimplements the same by-name-not-by-[Map] semantics
/// directly rather than taking a dependency on an API the library itself flags for removal.</para>
///
/// <para><b>Strict mapping (both directions).</b> A <c>[Map]</c>-decorated entity queried through
/// RepoDb's normal entity path is matched by its mapped name instead of the alias and silently
/// comes back with default values (the historical <c>/detail/00000000-…</c> bug) — living under
/// <c>Repositories/</c> does NOT put this class inside <c>RepositoryMappingTests</c>' guard for
/// that bug class, because that test only scans for RepoDb's own
/// <c>.ExecuteQueryAsync&lt;T&gt;</c>/<c>.QueryAsync&lt;T&gt;</c>/<c>.ExecuteScalarAsync&lt;T&gt;</c>
/// calls, and this class makes none of those. The protection has to come from the mapper itself,
/// so <see cref="MapRowsAsync{TEntity}"/> also throws if any of <typeparamref name="TRow"/>'s
/// writable properties has NO matching reader column — an alias typo (<c>AS "Provider_Id"</c> vs a
/// <c>ProviderId</c> property) is exactly the same silent-default bug, just spelled differently. A
/// reader column with no matching property is NOT an error — extra projection columns exist for
/// other reasons (ordering, grouping) and RepoDb's own mapping tolerates them too.</para>
///
/// <para><b>NULL into a non-nullable value-type property throws, never silently defaults.</b> A
/// LEFT JOIN can legitimately produce SQL NULL for any joined column, and
/// <see cref="PropertyInfo.SetValue(object?, object?)"/> assigning <c>null</c> to a non-nullable
/// value-type property does NOT throw on .NET — it silently writes <c>default(T)</c>
/// (<c>Guid.Empty</c>, <c>0</c>, ...). That is the exact bug class the no-<c>[Map]</c> rule above
/// exists to prevent, reachable the moment any projection LEFT JOINs (the Providers projection
/// already does). <see cref="SetPropertyValue"/> checks for this explicitly and throws, naming the
/// column and property, rather than letting the CLR's own permissive behavior smuggle a bad value
/// through.</para>
///
/// <para><b>Type coercion.</b> Reflection's <c>PropertyInfo.SetValue</c> requires an exact type
/// match — no numeric widening. But Npgsql's own returned CLR types do not always exactly match a
/// DTO's declared property type even when the value is perfectly representable (a PG
/// <c>integer</c> column into a <c>decimal</c> property — the dominant DTO shape in this codebase
/// — or an un-cast <c>COUNT(*)</c>/<c>SUM(int_col)</c>, which Npgsql returns as <c>long</c>, into
/// an <c>int</c> property). <see cref="SetPropertyValue"/> falls back to
/// <see cref="Convert.ChangeType(object?, Type, IFormatProvider?)"/> (the same mechanism RepoDb
/// and Dapper both use) for any <see cref="IConvertible"/> mismatch, and on failure throws naming
/// the column, its SQL-reported CLR type, and the target property/type — never a bare, anonymous
/// <see cref="ArgumentException"/> that <c>ErrorHandlingMiddleware</c> would otherwise map to an
/// HTTP 400 as if it were bad client input, when it is really an internal projection/DTO
/// mismatch.</para>
/// </summary>
public sealed class NpgsqlGridDataSource<TRow>
    : IGridDataSource<TRow>, IGridValueCountSource<TRow>, IGridGroupingSource<TRow>, IGridAggregateSource<TRow>
    where TRow : class
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly GridSqlMap _map;
    private readonly GridOptions<TRow> _options;

    public NpgsqlGridDataSource(NpgsqlDataSource dataSource, GridSqlMap map, GridOptions<TRow> options)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _map = map ?? throw new ArgumentNullException(nameof(map));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async ValueTask<PageResult<TRow>> LoadAsync(GridQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var (where, parameters) = GridSqlBuilder.BuildWhere(_map, query, null);
        var orderBy = GridSqlBuilder.BuildOrderBy(_map, query);

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);

        var total = await CountAsync(conn, where, parameters, cancellationToken);

        var page = query.Paging.Page < 1 ? 1 : query.Paging.Page;
        var size = query.Paging.PageSize < 1 ? 1 : query.Paging.PageSize;

        var sql = $"{_map.Projection} {where} {orderBy} LIMIT @__limit OFFSET @__offset";

        await using var cmd = CreateCommand(conn, sql, parameters);
        cmd.Parameters.Add(new NpgsqlParameter("__limit", size));
        cmd.Parameters.Add(new NpgsqlParameter("__offset", (page - 1) * size));

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var rows = await MapRowsAsync<TRow>(reader, cancellationToken);

        return new PageResult<TRow>(rows, total, page, size);
    }

    public async ValueTask<GridValueCounts> GetValuesAsync(
        GridColumn<TRow> column, GridQuery context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(context);

        if (!_map.Columns.TryGetValue(column.Field, out var sqlColumn))
        {
            return new GridValueCounts([], 0);
        }

        // Excel semantics: the column's own filter is excluded from its own value list.
        var (where, parameters) = GridSqlBuilder.BuildWhere(_map, context, excludeField: column.Field);
        var alias = sqlColumn.OutputAlias;

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);

        // Pre-cap distinct count FIRST (sequential on this one connection — no MARS) so the
        // client's "Showing N of M values" truncation banner reports the real M. Reporting
        // values.Count (post-LIMIT) here would make items.length == totalDistinct always true,
        // permanently disabling that banner — see the class doc's "Type coercion" neighbor note,
        // same "don't let a post-processing step silently launder a number" theme.
        var totalDistinct = await CountDistinctAsync(conn, alias, where, parameters, cancellationToken);

        // Wraps the projection as a derived table (`src`) with `where` INSIDE the parens, so the
        // builder's WHERE — written against the projection's own table aliases (p, pt, ...) —
        // stays in the scope where those aliases are visible. GROUP BY / SELECT then reference
        // `src."{OutputAlias}"`, the derived table's own output column, which IS visible outside
        // the parens (unlike the inner aliases). `, "Value"` is a deterministic tiebreaker on
        // `ORDER BY "Count" DESC` — without it, which rows survive a LIMIT below the actual
        // distinct-value count is arbitrary and can change between two openings of the same
        // popover (the same reasoning that makes GridSqlMap.DefaultOrderBy mandatory).
        var sql = $@"
            SELECT src.""{alias}""::text AS ""Value"", COUNT(*)::int AS ""Count""
            FROM ({_map.Projection} {where}) AS src
            GROUP BY src.""{alias}""
            ORDER BY ""Count"" DESC, ""Value""
            LIMIT @__limit";

        await using var cmd = CreateCommand(conn, sql, parameters);
        cmd.Parameters.Add(new NpgsqlParameter("__limit", _options.FilterValuesLimit));

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var rows = await MapRowsAsync<ValueCountRow>(reader, cancellationToken);

        var values = rows.Select(r => new GridValueCount(r.Value ?? string.Empty, r.Count)).ToList();
        return new GridValueCounts(values, totalDistinct);
    }

    public async ValueTask<GroupedPageResult<TRow>> LoadGroupedAsync(
        GridQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var (where, parameters) = GridSqlBuilder.BuildWhere(_map, query, null);
        var orderBy = GridSqlBuilder.BuildOrderBy(_map, query);

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);

        // Count FIRST so the cap is enforced without materializing anything (grouping
        // materializes the whole filtered set — spec §5.6).
        var total = await CountAsync(conn, where, parameters, cancellationToken);
        if (total > _map.MaxGroupingRows)
        {
            // GridQueryException (→ 400), not InvalidOperationException (→ unhandled, 500 + an
            // ERROR-level Serilog entry). Hitting this cap is an ORDINARY user action — tick
            // "group by Categoria" on an unfiltered list — not an internal fault, and paging the
            // on-call engineer for it while telling the user nothing is the wrong end of both.
            // Same reasoning, and the same exception type, as GridSqlBuilder's own rejections.
            //
            // The message interpolates only two integers, never user input: ArgumentException-family
            // .Message flows into ErrorHandlingMiddleware's 400 JSON body, and some callers splice
            // `data.message` into innerHTML (see GridSqlBuilder's note) — so echoing a filter value
            // here would be a reflected-XSS sink. Row counts are already visible in the grid footer.
            var limitMessage =
                $"Grouping would materialize {total} rows, above the limit of {_map.MaxGroupingRows}. " +
                "Narrow the filters and try again.";

            throw new GridQueryException(limitMessage, new Dictionary<string, string[]>
            {
                ["groupBy"] = [limitMessage],
            });
        }

        var sql = $"{_map.Projection} {where} {orderBy}";
        await using var cmd = CreateCommand(conn, sql, parameters);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var rows = await MapRowsAsync<TRow>(reader, cancellationToken);

        return GridGrouper<TRow>.Group(rows, query, _options);
    }

    /// <summary>Grand totals over the filtered set: one SELECT SUM/AVG/MIN/MAX, pushed down.</summary>
    public async ValueTask<GridAggregates> GetAggregatesAsync(GridQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var (where, parameters) = GridSqlBuilder.BuildWhere(_map, query, null);
        var (sql, slots) = GridSqlBuilder.BuildAggregateSelect(
            _map, where, _options.AggregateColumns.Select(static c => (c.Field, c.Aggregates)));

        if (slots.Count == 0)
        {
            return GridAggregates.Empty;
        }

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = CreateCommand(conn, sql, parameters);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);   // an aggregate SELECT without GROUP BY always returns one row

        var byField = new Dictionary<string, GridAggregateValues>(StringComparer.Ordinal);
        for (var i = 0; i < slots.Count; i++)
        {
            var (field, function) = slots[i];
            decimal? value = await reader.IsDBNullAsync(i, cancellationToken) ? null : reader.GetDecimal(i);
            var current = byField.GetValueOrDefault(field) ?? new GridAggregateValues(null, null, null, null);

            byField[field] = function switch
            {
                GridAggregate.Sum => current with { Sum = value },
                GridAggregate.Avg => current with { Avg = value },
                GridAggregate.Min => current with { Min = value },
                _ => current with { Max = value }
            };
        }

        return new GridAggregates(byField);
    }

    private async Task<int> CountAsync(
        NpgsqlConnection conn, string where, IReadOnlyList<NpgsqlParameter> parameters, CancellationToken cancellationToken)
    {
        // Wraps the projection as a derived table with `where` INSIDE the parens — see the
        // GetValuesAsync doc comment for why this replaced the earlier FROM-clause-extraction
        // approach: that approach mis-parsed any snake_case column ending or starting with
        // "from" (valid_FROM, FROM_status, ...) and silently produced wrong SQL or wrong counts
        // for a WITH/DISTINCT projection. COUNT(*) needs no inner-scoped column reference at
        // all, so the plain wrap works with zero parsing.
        var sql = $"SELECT COUNT(*)::int FROM ({_map.Projection} {where}) AS src";

        await using var cmd = CreateCommand(conn, sql, parameters);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is int count ? count : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private async Task<int> CountDistinctAsync(
        NpgsqlConnection conn, string outputAlias, string where, IReadOnlyList<NpgsqlParameter> parameters,
        CancellationToken cancellationToken)
    {
        // Must count the exact same set of buckets GetValuesAsync's `GROUP BY src."{alias}"`
        // produces — NOT `COUNT(DISTINCT col)`, which silently drops the NULL bucket (COUNT(...)
        // ignores NULL inputs; GROUP BY does not — it groups every NULL into its own bucket, the
        // same way `SELECT DISTINCT` does). A LEFT-JOINed filterable column with NULL rows would
        // otherwise under-report TotalDistinct by exactly 1, which can make a genuinely truncated
        // value list (one value cut by the LIMIT, e.g. the NULL bucket sorting last under
        // `ORDER BY "Value"` ASC) read as `values.Count == TotalDistinct` — "not truncated" — the
        // exact boundary the pre-cap-count fix above exists to get right.
        var sql = $@"
            SELECT COUNT(*)::int FROM (
                SELECT DISTINCT src.""{outputAlias}""
                FROM ({_map.Projection} {where}) AS src
            ) AS d";

        await using var cmd = CreateCommand(conn, sql, parameters);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is int count ? count : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Builds a command against <paramref name="conn"/> with a CLONE of every builder-produced
    /// parameter added to it. Cloning (rather than adding the original instances) is required
    /// because a single call into this class can issue multiple commands sharing the same WHERE
    /// clause (a COUNT, sometimes a COUNT DISTINCT, then a page SELECT/GROUP BY), and a
    /// <see cref="NpgsqlParameter"/> can only be owned by one <see cref="NpgsqlParameterCollection"/>
    /// at a time — reusing the original instance on a second command throws
    /// <c>"The NpgsqlParameter is already contained by another NpgsqlParameterCollection"</c>,
    /// and that guard fires even after the first command has already been disposed (the
    /// parameter, not the command, tracks ownership). <see cref="NpgsqlParameter.Clone"/> copies
    /// <c>NpgsqlDbType</c> and <c>Value</c> together, so the explicit array type set by
    /// <see cref="GridSqlBuilder"/> for <c>In</c> filters survives onto every command that needs
    /// it.
    /// </summary>
    private static NpgsqlCommand CreateCommand(
        NpgsqlConnection conn, string sql, IReadOnlyList<NpgsqlParameter> parameters)
    {
        var cmd = new NpgsqlCommand(sql, conn);
        foreach (var p in parameters)
        {
            cmd.Parameters.Add((NpgsqlParameter)p.Clone());
        }

        return cmd;
    }

    /// <summary>
    /// Materializes every remaining row of <paramref name="reader"/> as
    /// <typeparamref name="TEntity"/>, matching each column to a writable public instance
    /// property of the same name (<see cref="StringComparer.OrdinalIgnoreCase"/>) — never a
    /// <c>[Map]</c> attribute, so this only ever sees the SQL <c>AS</c> alias, exactly like the
    /// column list the projection actually produced. See the class-level doc for why this exists
    /// instead of RepoDb's own (obsolete) reader bridge, and for the strict-mapping /
    /// NULL-into-non-nullable / type-coercion rules <see cref="SetPropertyValue"/> enforces.
    /// </summary>
    private static async Task<List<TEntity>> MapRowsAsync<TEntity>(
        NpgsqlDataReader reader, CancellationToken cancellationToken) where TEntity : class
    {
        var properties = typeof(TEntity)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static p => p.CanWrite)
            .ToDictionary(static p => p.Name, static p => p, StringComparer.OrdinalIgnoreCase);

        var columns = new List<(int Ordinal, PropertyInfo Property)>(reader.FieldCount);
        var matchedPropertyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (properties.TryGetValue(reader.GetName(i), out var property))
            {
                columns.Add((i, property));
                matchedPropertyNames.Add(property.Name);
            }
        }

        // Strict mapping: a mapped property with NO matching reader column is an alias typo
        // (e.g. `AS "Provider_Id"` vs a `ProviderId` property) that would otherwise leave the
        // property silently at its default (Guid.Empty, null, 0) — the exact bug class [Map]
        // is banned to avoid. Computed once per query (not per row). A reader column with no
        // matching property is NOT an error — matches RepoDb's own tolerant behavior, and extra
        // projection columns legitimately exist for ordering/grouping without being read back.
        var unmatchedProperties = properties.Values
            .Where(p => !matchedPropertyNames.Contains(p.Name))
            .ToList();
        if (unmatchedProperties.Count > 0)
        {
            var available = string.Join(", ", Enumerable.Range(0, reader.FieldCount).Select(reader.GetName));
            var names = string.Join(", ", unmatchedProperties.Select(p => p.Name));
            throw new InvalidOperationException(
                $"{typeof(TEntity).Name} has propert{(unmatchedProperties.Count == 1 ? "y" : "ies")} " +
                $"with no matching SQL column: {names}. Available columns: {available}. Check the " +
                "projection's AS aliases for a typo.");
        }

        var rows = new List<TEntity>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var entity = Activator.CreateInstance<TEntity>();
            foreach (var (ordinal, property) in columns)
            {
                SetPropertyValue(reader, ordinal, property, entity);
            }

            rows.Add(entity);
        }

        return rows;
    }

    /// <summary>
    /// Assigns one reader column to one property, with two safety rules reflection's own
    /// <c>PropertyInfo.SetValue</c> does not enforce on its own:
    ///
    /// <para><b>SQL NULL into a non-nullable value-type property throws.</b>
    /// <c>SetValue(entity, null)</c> against a non-nullable value-type property (e.g.
    /// <c>Guid ProviderTypeId</c>) does not throw on .NET — it silently writes
    /// <c>default(T)</c>. A LEFT JOIN can produce NULL for any joined column, so this is
    /// reachable in production the moment a projection LEFT JOINs, and it is exactly the
    /// silent-default bug ([Map]'s alias mismatch) the rest of this class exists to prevent.</para>
    ///
    /// <para><b>Type mismatches are coerced via <see cref="Convert.ChangeType(object?, Type, IFormatProvider?)"/>,
    /// not left to throw anonymously.</b> Reflection's <c>SetValue</c> requires an exact type
    /// match — no <c>int → decimal</c>, no <c>long → int</c> — but Npgsql's returned CLR type does
    /// not always exactly equal a DTO's declared property type even for a perfectly representable
    /// value (an <c>integer</c> column into a <c>decimal</c> property is the dominant DTO shape in
    /// this codebase; an un-cast <c>COUNT(*)</c>/<c>SUM(int_col)</c> comes back as <c>long</c>).
    /// On a genuine, unrepresentable mismatch this throws a message naming the column, its
    /// SQL-reported CLR type, and the target property/type — never a bare <see cref="ArgumentException"/>
    /// that <c>ErrorHandlingMiddleware</c> would otherwise report to the client as a 400, when it
    /// is really an internal projection/DTO bug.</para>
    /// </summary>
    private static void SetPropertyValue(NpgsqlDataReader reader, int ordinal, PropertyInfo property, object entity)
    {
        var propertyType = property.PropertyType;
        var underlyingType = Nullable.GetUnderlyingType(propertyType);
        var isNonNullableValueType = underlyingType is null && propertyType.IsValueType;
        var columnName = reader.GetName(ordinal);

        if (reader.IsDBNull(ordinal))
        {
            if (isNonNullableValueType)
            {
                throw new InvalidOperationException(
                    $"Column \"{columnName}\" is NULL but {property.DeclaringType?.Name}.{property.Name} " +
                    $"is the non-nullable value type {propertyType.Name}. Declare the property as " +
                    $"{propertyType.Name}?, or add COALESCE(...) to the SQL projection so this " +
                    "column is never NULL — a LEFT JOIN producing NULL here would otherwise " +
                    $"silently become default({propertyType.Name}).");
            }

            property.SetValue(entity, null);
            return;
        }

        var rawValue = reader.GetValue(ordinal);
        var targetType = underlyingType ?? propertyType;

        if (targetType.IsInstanceOfType(rawValue))
        {
            property.SetValue(entity, rawValue);
            return;
        }

        if (rawValue is IConvertible)
        {
            try
            {
                var converted = Convert.ChangeType(rawValue, targetType, CultureInfo.InvariantCulture);
                property.SetValue(entity, converted);
                return;
            }
            catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
            {
                throw new InvalidOperationException(
                    $"Column \"{columnName}\" (SQL type {reader.GetFieldType(ordinal).Name}, value " +
                    $"type {rawValue.GetType().Name}) cannot be converted to " +
                    $"{property.DeclaringType?.Name}.{property.Name} ({propertyType.Name}).", ex);
            }
        }

        throw new InvalidOperationException(
            $"Column \"{columnName}\" (SQL type {reader.GetFieldType(ordinal).Name}) cannot be " +
            $"assigned to {property.DeclaringType?.Name}.{property.Name} ({propertyType.Name}) — " +
            "value is not IConvertible.");
    }

    private sealed class ValueCountRow
    {
        public string? Value { get; set; }
        public int Count { get; set; }
    }
}
