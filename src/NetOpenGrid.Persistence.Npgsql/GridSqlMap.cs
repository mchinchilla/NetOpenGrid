using System.Data;

namespace NetOpenGrid.Persistence.Npgsql;

/// <summary>One whitelisted, sortable/filterable column: grid field → SQL expression.</summary>
/// <param name="SqlExpression">Qualified SQL expression, e.g. <c>p.company_name</c>. Never user input.</param>
/// <param name="ClrType">Type a filter value is parsed to before it is bound as a parameter.</param>
/// <param name="OutputAlias">
/// The PascalCase <c>AS</c> alias this column carries in <see cref="GridSqlMap.Projection"/>'s own
/// SELECT list — e.g. <c>"ProviderTypeName"</c> for <c>pt.name AS "ProviderTypeName"</c>. Required,
/// and deliberately NOT derived from <see cref="SqlExpression"/> by convention (no reliable
/// mechanical mapping from <c>pt.name</c> to <c>"ProviderTypeName"</c> exists). A query that needs
/// a DIFFERENT select list over the same projection (value-counts' <c>GROUP BY</c>) must wrap
/// <see cref="GridSqlMap.Projection"/> as a derived table to reuse its JOINs without hand-copying
/// them — and once wrapped, only the derived table's OWN output aliases are visible to the outer
/// query, never the projection's inner table aliases (<c>p</c>, <c>pt</c>, ...) that
/// <see cref="SqlExpression"/> is written against. See
/// <c>NpgsqlGridDataSource&lt;TRow&gt;.GetValuesAsync</c>.
/// </param>
/// <param name="Searchable">Included in the global <c>q</c> OR-search.</param>
public sealed record GridSqlColumn(string SqlExpression, Type ClrType, string OutputAlias, bool Searchable = false);

/// <summary>
/// Immutable SQL contract for one grid, built once at startup.
///
/// <para><b>This is the whitelist.</b> Identifiers reaching SQL text come only from
/// <see cref="Columns"/>; every value is bound as an <c>NpgsqlParameter</c>. See spec §5.3.</para>
///
/// <para><b>One definition rule (spec §11.2):</b> <see cref="Projection"/> must reference the owning
/// repository's single <c>const</c> SELECT, never a copy of it.</para>
/// </summary>
/// <param name="Projection">SELECT … FROM … JOIN …, with no WHERE/ORDER BY/LIMIT.</param>
/// <param name="BaseWhere">
/// Always-applied predicate, e.g. <c>p.active = TRUE</c>. <see cref="GridSqlBuilder.BuildWhere"/>
/// wraps it in parentheses before ANDing it with filters, so a top-level <c>OR</c> here is not
/// silently defeated by <c>AND</c>'s tighter precedence — cannot be filtered away. Cannot be
/// empty; spell "no base predicate" as the literal <c>"TRUE"</c>.
/// </param>
/// <param name="Columns">Grid field → SQL. Case-sensitive, matching NetOpenGrid field ids.</param>
/// <param name="DefaultOrderBy">
/// Deterministic tiebreaker appended to every ORDER BY. Mandatory: LIMIT/OFFSET over a
/// non-total order silently repeats and drops rows across pages. Cannot be empty.
/// </param>
/// <param name="MaxGroupingRows">
/// Grouping materializes the whole filtered set (spec §5.6). Above this count the source
/// fails loudly instead of eating memory — with a <c>GridQueryException</c> (400), because
/// the user asking for it is an ordinary interaction, not an internal fault.
/// </param>
public sealed record GridSqlMap(
    string Projection,
    string BaseWhere,
    IReadOnlyDictionary<string, GridSqlColumn> Columns,
    string DefaultOrderBy,
    int MaxGroupingRows = 5000)
{
    // Validated property overrides of the primary constructor's parameters (the standard C#
    // record pattern for constructor validation: the unqualified `BaseWhere`/`DefaultOrderBy`
    // on the right-hand side of each initializer binds to the primary constructor's parameter,
    // not to the property being declared). Runs for `new GridSqlMap(...)`; a record's `with`
    // clone uses a separate compiler-generated copy constructor and does NOT re-run these
    // initializers — acceptable here because a GridSqlMap is built once at startup from a
    // literal, not mutated via `with` at request time.

    /// <inheritdoc cref="GridSqlMap"/>
    public string BaseWhere { get; init; } = string.IsNullOrWhiteSpace(BaseWhere)
        ? throw new ArgumentException(
            "BaseWhere cannot be empty or whitespace — an empty predicate would emit "
            + "\"WHERE  AND ...\" (a syntax error). Spell \"no base predicate\" as the "
            + "literal \"TRUE\".",
            nameof(BaseWhere))
        : BaseWhere;

    /// <inheritdoc cref="GridSqlMap"/>
    public string DefaultOrderBy { get; init; } = string.IsNullOrWhiteSpace(DefaultOrderBy)
        ? throw new ArgumentException(
            "DefaultOrderBy cannot be empty or whitespace — every grid needs a "
            + "deterministic tiebreaker, and an empty value would emit a trailing comma "
            + "in ORDER BY (a syntax error).",
            nameof(DefaultOrderBy))
        : DefaultOrderBy;

    /// <inheritdoc cref="GridSqlMap"/>
    public IReadOnlyDictionary<string, GridSqlColumn> Columns { get; init; } =
        ValidateOutputAliasesAppearInProjection(Columns, Projection);

    /// <summary>
    /// One-directional, deliberately dumb startup check: every column's
    /// <see cref="GridSqlColumn.OutputAlias"/> must appear as a literal substring somewhere in
    /// <paramref name="projection"/>. This is the entire check — no SQL parsing, no understanding
    /// of where the projection's SELECT list ends or which "AS" belongs to which expression. That
    /// is deliberate: a fuller "does this alias actually apply to this column" parser is exactly
    /// the kind of fragile SQL-text surgery <c>NpgsqlGridDataSource.ExtractFromClause</c> used to
    /// attempt and was deleted for (it mis-parsed real snake_case columns like <c>valid_from</c>).
    /// A substring search cannot make that mistake, because it never tries to understand the
    /// projection's structure at all — the trade-off is it cannot catch every mistake (an
    /// <em>unquoted</em> alias like <c>AS CompanyName</c> still contains the substring
    /// <c>"CompanyName"</c> even though PostgreSQL folds it to lowercase at runtime), only the
    /// "wrong or missing alias entirely" class, which is the common typo. Zero false positives for
    /// a correctly quoted column, by construction: the alias text always appears verbatim inside
    /// <c>AS "Alias"</c> in a correct projection.
    /// </summary>
    private static IReadOnlyDictionary<string, GridSqlColumn> ValidateOutputAliasesAppearInProjection(
        IReadOnlyDictionary<string, GridSqlColumn> columns, string projection)
    {
        foreach (var (field, column) in columns)
        {
            if (!projection.Contains(column.OutputAlias, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Column \"{field}\" declares OutputAlias \"{column.OutputAlias}\", which does " +
                    "not appear anywhere in Projection. Either Projection is missing this column's " +
                    "AS alias, or the alias text doesn't match exactly — check for a typo, and "
                    + "remember an unquoted alias is folded to lowercase by PostgreSQL "
                    + $"(quote it: AS \"{column.OutputAlias}\").",
                    nameof(Columns));
            }
        }

        return columns;
    }
}
