using System.Text.Json;
using System.Text.RegularExpressions;
using NetOpenGrid.Domain.GridQuerying;
using NpgsqlTypes;
using NetOpenGrid.Persistence.Npgsql;
using Xunit;

namespace NetOpenGrid.Persistence.Npgsql.Tests;

public class GridSqlBuilderTests
{
    // Every column's OutputAlias must be a real, quoted alias in Projection — PostgreSQL folds
    // an UNQUOTED "AS CompanyName" to lowercase (companyname), so this fixture used to declare
    // OutputAlias values ("Code", "CompanyName", "CreditLimit", "CreatedDate" — see the
    // Map()-with(...) extensions below) that either weren't quoted or weren't in the projection
    // at all (credit_limit/creation_date weren't selected). Latent only because this is a pure
    // unit-test fixture that never touches a database — GridSqlBuilder itself never reads
    // OutputAlias, so nothing here failed until GridSqlMap's constructor started validating it
    // (see ValidateOutputAliasesAppearInProjection). Fixed: quoted every alias, and selected the
    // two previously-missing columns so every alias declared anywhere in this file is real.
    private static GridSqlMap Map() => new(
        Projection: @"SELECT p.code AS ""Code"", p.company_name AS ""CompanyName"", "
            + @"p.credit_limit AS ""CreditLimit"", p.creation_date AS ""CreatedDate"" FROM providers p",
        BaseWhere: "p.active = TRUE",
        Columns: new Dictionary<string, GridSqlColumn>
        {
            ["code"] = new("p.code", typeof(string), OutputAlias: "Code", Searchable: true),
            ["companyName"] = new("p.company_name", typeof(string), OutputAlias: "CompanyName", Searchable: true),
            ["creditLimit"] = new("p.credit_limit", typeof(decimal), OutputAlias: "CreditLimit", Searchable: false),
        },
        DefaultOrderBy: "p.provider_id");

    private static GridQuery Q(params FilterDescriptor[] filters) =>
        GridQuery.Empty with { Filters = filters };

    [Fact]
    public void Contains_Binds_The_Wildcards_As_A_Parameter()
    {
        var (sql, ps) = GridSqlBuilder.BuildWhere(Map(), Q(new FilterDescriptor("companyName", FilterOperator.Contains, "acme")), null);

        Assert.Contains("p.company_name ILIKE @p0", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("acme", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("%acme%", Assert.Single(ps).Value);
    }

    [Fact]
    public void Base_Where_Is_Always_Present()
    {
        var (sql, _) = GridSqlBuilder.BuildWhere(Map(), GridQuery.Empty, null);
        Assert.Contains("p.active = TRUE", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void BaseWhere_With_A_Top_Level_OR_Is_Not_Defeated_By_A_Later_AND()
    {
        // Without parens, "WHERE A OR B AND filter" parses as "A OR (B AND filter)" — the "A"
        // disjunct escapes the filter entirely. Parenthesizing BaseWhere as a whole prevents that.
        var map = Map() with { BaseWhere = "p.active = TRUE OR p.is_special = TRUE" };
        var (sql, _) = GridSqlBuilder.BuildWhere(map, Q(new FilterDescriptor("code", FilterOperator.Equals, "A1")), null);

        Assert.StartsWith("WHERE (p.active = TRUE OR p.is_special = TRUE) AND p.code = @p0", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void GridSqlMap_Rejects_Empty_BaseWhere()
    {
        // Must exercise `new GridSqlMap(...)` directly — a `with` expression uses the
        // compiler-generated copy constructor, which does not re-run these validations.
        Assert.Throws<ArgumentException>(() => new GridSqlMap(
            Projection: "SELECT 1",
            BaseWhere: "   ",
            Columns: new Dictionary<string, GridSqlColumn>(),
            DefaultOrderBy: "id"));
    }

    [Fact]
    public void GridSqlMap_Rejects_Empty_DefaultOrderBy()
    {
        Assert.Throws<ArgumentException>(() => new GridSqlMap(
            Projection: "SELECT 1",
            BaseWhere: "TRUE",
            Columns: new Dictionary<string, GridSqlColumn>(),
            DefaultOrderBy: ""));
    }

    [Fact]
    public void GridSqlMap_Rejects_A_Column_Whose_OutputAlias_Is_Not_In_Projection()
    {
        // Fix round 2, item 2: the one-directional startup check — a column's OutputAlias must
        // appear as a literal substring somewhere in Projection, or construction throws. Catches
        // a missing/mistyped alias at boot instead of a 42703 at query time.
        var ex = Assert.Throws<ArgumentException>(() => new GridSqlMap(
            Projection: @"SELECT p.code AS ""Code"" FROM providers p",
            BaseWhere: "TRUE",
            Columns: new Dictionary<string, GridSqlColumn>
            {
                ["code"] = new("p.code", typeof(string), OutputAlias: "Code"),
                ["companyName"] = new("p.company_name", typeof(string), OutputAlias: "CompanyName"),
            },
            DefaultOrderBy: "p.provider_id"));

        Assert.Contains("companyName", ex.Message, StringComparison.Ordinal);
        Assert.Contains("CompanyName", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_Field_Throws_A_GridQueryException_With_A_Constant_Message()
    {
        // GridQueryException.Message (this is what GridSqlBuilder actually throws — an earlier
        // draft threw ArgumentOutOfRangeException, and this comment still named that type) flows
        // straight into ErrorHandlingMiddleware's 400 body, and bulk-import-resolve-catalogs.js
        // splices `data.message` into innerHTML unescaped — so the message must be constant,
        // never the raw field text. The field name is still available, but only as an Errors
        // dictionary KEY (consumed via .textContent elsewhere, not innerHTML), matching
        // CLAUDE.md's GridQueryException field-error shape.
        var q = Q(new FilterDescriptor("p.code; DROP TABLE providers --", FilterOperator.Equals, "x"));
        var ex = Assert.Throws<GridQueryException>(() => GridSqlBuilder.BuildWhere(Map(), q, null));

        Assert.Equal("The grid request is not valid.", ex.Message);
        Assert.Contains("p.code; DROP TABLE providers --", ex.Errors.Keys);
    }

    [Fact]
    public void Value_Is_Parsed_To_The_Column_Clr_Type()
    {
        var (_, ps) = GridSqlBuilder.BuildWhere(Map(), Q(new FilterDescriptor("creditLimit", FilterOperator.GreaterThan, "1500.50")), null);
        Assert.Equal(1500.50m, Assert.Single(ps).Value);
    }

    [Fact]
    public void Unparsable_Value_Throws_A_GridQueryException_With_A_Constant_Message()
    {
        // Parse's raw FormatException used to fall through to ErrorHandlingMiddleware's default
        // 500 arm (unhandled exception, ERROR-level Serilog entry) for what is a client-input
        // format problem, not an internal bug. Wrapped in GridQueryException — same constant
        // message + field-as-key pattern as the unknown-field case — it's now a 400 instead.
        var q = Q(new FilterDescriptor("creditLimit", FilterOperator.GreaterThan, "not-a-number"));
        var ex = Assert.Throws<GridQueryException>(() => GridSqlBuilder.BuildWhere(Map(), q, null));

        Assert.Equal("The grid request is not valid.", ex.Message);
        Assert.Contains("creditLimit", ex.Errors.Keys);
    }

    [Fact]
    public void DateTime_Value_Parses_With_Utc_Kind()
    {
        // AdjustToUniversal alone leaves Kind=Unspecified for an offset-less string, which
        // Npgsql refuses to write to a `timestamptz` column. AssumeUniversal + AdjustToUniversal
        // together guarantee Kind=Utc regardless of whether the input carries an offset.
        var map = Map() with
        {
            Columns = new Dictionary<string, GridSqlColumn>(Map().Columns)
            {
                ["createdDate"] = new("p.creation_date", typeof(DateTime), OutputAlias: "CreatedDate"),
            },
        };
        var (_, ps) = GridSqlBuilder.BuildWhere(map, Q(new FilterDescriptor("createdDate", FilterOperator.GreaterThan, "2026-01-15 10:30")), null);

        var bound = (DateTime)Assert.Single(ps).Value!;
        Assert.Equal(DateTimeKind.Utc, bound.Kind);
    }

    [Fact]
    public void IsEmpty_Emits_Null_Or_Blank_And_Binds_Nothing()
    {
        var (sql, ps) = GridSqlBuilder.BuildWhere(Map(), Q(new FilterDescriptor("code", FilterOperator.IsEmpty, null)), null);

        Assert.Contains("(p.code IS NULL OR p.code = '')", sql, StringComparison.Ordinal);
        Assert.Empty(ps);
    }

    [Fact]
    public void Contains_On_A_NonText_Column_Casts_To_Text()
    {
        // creditLimit is decimal — ILIKE against it raw would 500 in Postgres
        // ("operator does not exist: numeric ~~* text"). Must cast to ::text first.
        var (sql, ps) = GridSqlBuilder.BuildWhere(Map(), Q(new FilterDescriptor("creditLimit", FilterOperator.Contains, "150")), null);

        Assert.Contains("p.credit_limit::text ILIKE @p0", sql, StringComparison.Ordinal);
        Assert.Equal("%150%", Assert.Single(ps).Value);
    }

    [Fact]
    public void IsEmpty_On_A_NonText_Column_Casts_To_Text()
    {
        var (sql, ps) = GridSqlBuilder.BuildWhere(Map(), Q(new FilterDescriptor("creditLimit", FilterOperator.IsEmpty, null)), null);

        Assert.Contains("(p.credit_limit IS NULL OR p.credit_limit::text = '')", sql, StringComparison.Ordinal);
        Assert.Empty(ps);
    }

    [Fact]
    public void Contains_Escapes_Percent_And_Underscore_As_Literal_Characters()
    {
        // Unescaped, "50%" is a LIKE *pattern* ("50" followed by anything) and "a_c" matches
        // "abc". Escaping makes them literal substrings again.
        var (_, ps) = GridSqlBuilder.BuildWhere(Map(), Q(new FilterDescriptor("companyName", FilterOperator.Contains, "50%_off")), null);

        Assert.Equal("%50\\%\\_off%", Assert.Single(ps).Value);
    }

    [Fact]
    public void EndsWith_Value_Ending_In_Backslash_Does_Not_End_The_Pattern_On_An_Escape_Character()
    {
        // Unescaped, this pattern would be "%foo\" — Postgres rejects a LIKE pattern that ends
        // on a bare escape character with SQLSTATE 22025. Escaping the backslash first avoids it.
        var (_, ps) = GridSqlBuilder.BuildWhere(Map(), Q(new FilterDescriptor("companyName", FilterOperator.EndsWith, "foo\\")), null);

        var pattern = (string)Assert.Single(ps).Value!;
        Assert.EndsWith("\\\\", pattern, StringComparison.Ordinal);
    }

    [Fact]
    public void Search_Term_Escapes_Like_Metacharacters()
    {
        var (_, ps) = GridSqlBuilder.BuildWhere(Map(), GridQuery.Empty with { Search = "50%_off" }, null);
        Assert.Equal("%50\\%\\_off%", Assert.Single(ps).Value);
    }

    [Fact]
    public void Search_Term_Is_Capped_Before_Being_Ored_Across_Every_Searchable_Column()
    {
        // An uncapped leading-% pattern ORed across every searchable column is a cheap CPU
        // sink (no index can help a leading wildcard). 500 plain characters, no metacharacters
        // to escape: the bound value should be exactly the cap plus the two wrapping '%'.
        var (_, ps) = GridSqlBuilder.BuildWhere(Map(), GridQuery.Empty with { Search = new string('a', 500) }, null);

        var bound = (string)Assert.Single(ps).Value!;
        Assert.Equal(202, bound.Length);
    }

    [Fact]
    public void In_Binds_A_Typed_Array_For_A_String_Column()
    {
        var (sql, ps) = GridSqlBuilder.BuildWhere(Map(), Q(new FilterDescriptor("code", FilterOperator.In, "[\"A1\",\"B2\"]")), null);

        Assert.Contains("p.code = ANY(@p0)", sql, StringComparison.Ordinal);
        var bound = Assert.Single(ps);
        Assert.Equal(NpgsqlDbType.Array | NpgsqlDbType.Text, bound.NpgsqlDbType);
        Assert.Equal(new[] { "A1", "B2" }, (string[])bound.Value!);
    }

    [Fact]
    public void In_Binds_A_Typed_Array_For_A_Decimal_Column()
    {
        var (sql, ps) = GridSqlBuilder.BuildWhere(Map(), Q(new FilterDescriptor("creditLimit", FilterOperator.In, "[\"100.50\",\"200\"]")), null);

        Assert.Contains("p.credit_limit = ANY(@p0)", sql, StringComparison.Ordinal);
        var bound = Assert.Single(ps);
        Assert.Equal(new[] { 100.50m, 200m }, (decimal[])bound.Value!);
    }

    [Fact]
    public void In_With_Empty_Array_Binds_A_Zero_Length_Typed_Array()
    {
        // Not malformed: a valid empty JSON array resolves to `= ANY('{}')`, which correctly
        // matches no rows — the whole reason `= ANY(array)` was kept over `IN (@p0, ...)`.
        var (sql, ps) = GridSqlBuilder.BuildWhere(Map(), Q(new FilterDescriptor("code", FilterOperator.In, "[]")), null);

        Assert.Contains("p.code = ANY(@p0)", sql, StringComparison.Ordinal);
        var bound = Assert.Single(ps);
        Assert.Empty((string[])bound.Value!);
    }

    [Fact]
    public void In_Value_Containing_A_Pipe_Character_Survives_Intact()
    {
        // The old '|'-split implementation would have chopped "a|b" into two elements. The
        // payload is JSON, so '|' has no special meaning here at all.
        var (_, ps) = GridSqlBuilder.BuildWhere(Map(), Q(new FilterDescriptor("code", FilterOperator.In, "[\"a|b\",\"c\"]")), null);

        var bound = Assert.Single(ps);
        Assert.Equal(new[] { "a|b", "c" }, (string[])bound.Value!);
    }

    [Fact]
    public void In_Malformed_Payload_Throws_A_GridQueryException_Rather_Than_Silently_Matching_Nothing()
    {
        // Silently producing zero rows on bad input is the exact CRITICAL-1 failure mode this
        // fix replaces (a user ticking two boxes and getting a silently empty grid, no error
        // anywhere). Fail loud instead — and as a 400 GridQueryException (client-input shape
        // problem), not an unhandled-exception 500: the raw JsonException is caught and wrapped
        // by AppendCondition, same constant-message + field-as-key pattern as every other
        // client-input error in this file.
        var q = Q(new FilterDescriptor("code", FilterOperator.In, "not-json"));
        var ex = Assert.Throws<GridQueryException>(() => GridSqlBuilder.BuildWhere(Map(), q, null));

        Assert.Equal("The grid request is not valid.", ex.Message);
        Assert.Contains("code", ex.Errors.Keys);
    }

    [Fact]
    public void In_Null_Payload_Throws_A_GridQueryException()
    {
        var q = Q(new FilterDescriptor("code", FilterOperator.In, "null"));
        Assert.Throws<GridQueryException>(() => GridSqlBuilder.BuildWhere(Map(), q, null));
    }

    [Fact]
    public void Every_Bound_Parameter_Name_In_Sql_Has_Exactly_One_Matching_Parameter()
    {
        // Pins the "@pN is unique by construction" invariant: extract every @p\d+ token from
        // the emitted SQL and set-compare against the actually-bound parameter names, for a
        // query exercising 2+ value-binding filters plus a search term (3 independent Bind calls).
        var q = Q(
            new FilterDescriptor("companyName", FilterOperator.Contains, "acme"),
            new FilterDescriptor("creditLimit", FilterOperator.GreaterThan, "100")
        ) with { Search = "corp" };

        var (sql, ps) = GridSqlBuilder.BuildWhere(Map(), q, null);

        var namesInSql = Regex.Matches(sql, @"@p\d+").Select(m => m.Value.TrimStart('@')).ToHashSet();
        var namesInParams = ps.Select(p => p.ParameterName.TrimStart('@')).ToHashSet();

        Assert.Equal(3, namesInSql.Count);
        Assert.True(namesInSql.SetEquals(namesInParams), $"SQL names [{string.Join(",", namesInSql)}] vs bound param names [{string.Join(",", namesInParams)}]");
        Assert.Equal(namesInSql.Count, ps.Count); // no duplicate names collapsed into the set
    }

    [Fact]
    public void ExcludeField_Drops_That_Columns_Own_Filter()
    {
        var q = Q(new("code", FilterOperator.Equals, "A1"), new("companyName", FilterOperator.Contains, "acme"));
        var (sql, _) = GridSqlBuilder.BuildWhere(Map(), q, excludeField: "code");

        Assert.DoesNotContain("p.code =", sql, StringComparison.Ordinal);
        Assert.Contains("p.company_name ILIKE", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Search_Ors_Across_Searchable_Columns_Only()
    {
        var (sql, ps) = GridSqlBuilder.BuildWhere(Map(), GridQuery.Empty with { Search = "acme" }, null);

        Assert.Contains("p.code ILIKE", sql, StringComparison.Ordinal);
        Assert.Contains("p.company_name ILIKE", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("p.credit_limit ILIKE", sql, StringComparison.Ordinal);
        Assert.Single(ps);
    }

    [Fact]
    public void Search_Casts_NonText_Searchable_Columns_To_Text()
    {
        // A local map (not the shared Map()) with creditLimit opted into search, to prove the
        // search-OR clause applies the same per-column cast rule as explicit filters, without
        // disturbing Search_Ors_Across_Searchable_Columns_Only's "not searchable" assertion.
        var map = Map() with
        {
            Columns = new Dictionary<string, GridSqlColumn>(Map().Columns)
            {
                ["creditLimit"] = new("p.credit_limit", typeof(decimal), OutputAlias: "CreditLimit", Searchable: true),
            },
        };

        var (sql, ps) = GridSqlBuilder.BuildWhere(map, GridQuery.Empty with { Search = "acme" }, null);

        Assert.Contains("p.code ILIKE", sql, StringComparison.Ordinal);
        Assert.Contains("p.credit_limit::text ILIKE", sql, StringComparison.Ordinal);
        Assert.Single(ps);
    }

    [Fact]
    public void OrderBy_Always_Ends_With_The_Deterministic_Tiebreaker()
    {
        var q = GridQuery.Empty with { Sorts = new[] { new SortDescriptor("companyName", SortDirection.Descending) } };
        var sql = GridSqlBuilder.BuildOrderBy(Map(), q);

        Assert.Equal("ORDER BY p.company_name DESC, p.provider_id", sql);
    }

    [Fact]
    public void OrderBy_With_No_Sorts_Is_Just_The_Tiebreaker()
    {
        Assert.Equal("ORDER BY p.provider_id", GridSqlBuilder.BuildOrderBy(Map(), GridQuery.Empty));
    }

    [Fact]
    public void OrderBy_Ignores_Unknown_Sort_Fields()
    {
        var q = GridQuery.Empty with { Sorts = new[] { new SortDescriptor("nope; DROP TABLE x", SortDirection.Ascending) } };
        Assert.Equal("ORDER BY p.provider_id", GridSqlBuilder.BuildOrderBy(Map(), q));
    }
}
