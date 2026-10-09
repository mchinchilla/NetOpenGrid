using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Domain.GridQuerying;
using NetOpenGrid.Domain.Results;

namespace NetOpenGrid.Persistence;

/// <summary>
/// Grouping pushed down to SQL. Each level is a <c>GROUP BY</c> on the column's selector with a
/// <c>COUNT</c>, ordered and (top level) paged in the database; only the groups the URL expands go
/// back to the database, for their sub-groups or for their rows (capped at
/// <see cref="GridOptions{T}.MaxGroupRows"/>). No query ever materializes the whole filtered set, so
/// grouping a table of millions costs one aggregate scan instead of loading every row into memory.
/// <para>
/// The result has the same shape as <c>GridGrouper</c>'s in-memory tree: the same raw keys in
/// <see cref="GridGroup{T}.Value"/> and <see cref="GridGroup{T}.Path"/> (so <c>expand=</c> links work
/// across engines), blanks first when ascending, strings with null and "" in one blank bucket, and
/// subtotals over every row of the group. Group order follows the database's ordering of the key
/// (typed: numbers and dates in their natural order, strings by the column's collation).
/// </para>
/// </summary>
internal static class EFGridGrouping<T> where T : class
{
    private static readonly MethodInfo LevelCoreMethod =
        typeof(EFGridGrouping<T>).GetMethod(nameof(LevelCoreAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>Every group column needs an expression selector; otherwise the caller groups in memory.</summary>
    public static bool CanPushDown(GridQuery query, GridOptions<T> options) =>
        Resolve(query, options).All(static c => c.SelectorExpression is not null && c.RawKeyFormatter is not null);

    public static async ValueTask<GroupedPageResult<T>> LoadAsync(
        IQueryable<T> filtered,
        GridQuery query,
        GridOptions<T> options,
        Func<IQueryable<T>, IQueryable<T>> sortRows,
        EFGridAggregateProjection<T>? aggregates,
        CancellationToken cancellationToken)
    {
        var paging = query.Paging.Normalized(options.MaxPageSize);
        var columns = Resolve(query, options);
        if (columns.Count == 0)
        {
            return new GroupedPageResult<T>([], 0, paging.Page, paging.PageSize);
        }

        var context = new Context(
            query,
            columns,
            query.Expanded.ToHashSet(StringComparer.Ordinal),
            sortRows,
            options.ShowsAggregates(GridAggregateRows.GroupHeader | GridAggregateRows.GroupFooter) ? aggregates : null,
            Math.Max(options.MaxGroupRows, 1),
            cancellationToken);

        var (groups, total) = await LevelAsync(context, filtered, 0, string.Empty, paging.Skip, paging.PageSize);
        return new GroupedPageResult<T>(groups, total, paging.Page, paging.PageSize);
    }

    /// <summary>Same rule as the in-memory grouper: known columns that can produce a raw key.</summary>
    private static List<GridColumn<T>> Resolve(GridQuery query, GridOptions<T> options)
    {
        var columns = new List<GridColumn<T>>();
        foreach (var field in query.GroupFields)
        {
            if (options.TryGetColumn(field, out var column) && column.RawKeyFormat is not null)
            {
                columns.Add(column);
            }
        }

        return columns;
    }

    /// <summary>One level, typed by the column's key (the selector's return type).</summary>
    private static Task<(List<GridGroup<T>> Groups, int Total)> LevelAsync(
        Context context, IQueryable<T> source, int level, string parentPath, int? skip, int? take) =>
        (Task<(List<GridGroup<T>>, int)>)LevelCoreMethod
            .MakeGenericMethod(context.Columns[level].SelectorExpression!.ReturnType)
            .Invoke(null, [context, source, level, parentPath, skip, take])!;

    private static async Task<(List<GridGroup<T>> Groups, int Total)> LevelCoreAsync<TKey>(
        Context context, IQueryable<T> source, int level, string parentPath, int? skip, int? take)
    {
        var ct = context.CancellationToken;
        var column = context.Columns[level];
        var key = KeySelector<TKey>(column.SelectorExpression!);
        var descending = context.Query.Sorts.FirstOrDefault(s => s.Field == column.Field)?.Direction == SortDirection.Descending;

        var counts = source
            .GroupBy(key)
            .Select(g => new GroupKeyCount<TKey> { Key = g.Key, Count = g.Count() });
        IQueryable<GroupKeyCount<TKey>> page = Order(counts, descending);
        if (skip is > 0)
        {
            page = page.Skip(skip.Value);
        }

        if (take is { } size)
        {
            page = page.Take(size);
        }

        var rows = await page.ToListAsync(ct);

        // A page that is not full is the last one: the total follows without a second query. Only a
        // full page (or one past the end) needs the COUNT(DISTINCT key).
        var offset = skip ?? 0;
        var total = take is null || (rows.Count < take && (rows.Count > 0 || offset == 0))
            ? offset + rows.Count
            : await source.Select(key).Distinct().CountAsync(ct);

        var groups = new List<GridGroup<T>>(rows.Count);
        foreach (var row in rows)
        {
            var raw = column.RawKeyFormatter!(row.Key) ?? string.Empty;
            var formatted = column.KeyFormatter?.Invoke(row.Key);
            var label = string.IsNullOrWhiteSpace(formatted) ? raw : formatted;
            var segment = $"{column.Field}={Uri.EscapeDataString(raw)}";
            var path = parentPath.Length == 0 ? segment : $"{parentPath}|{segment}";
            var members = source.Where(KeyEquals(key, row.Key));

            IReadOnlyList<GridGroup<T>> children = [];
            IReadOnlyList<T> leafRows = [];
            if (context.Expanded.Contains(path))
            {
                if (level + 1 < context.Columns.Count)
                {
                    children = (await LevelAsync(context, members, level + 1, path, null, null)).Groups;
                }
                else
                {
                    leafRows = await context.SortRows(members).Take(context.MaxGroupRows).ToListAsync(ct);
                }
            }

            groups.Add(new GridGroup<T>(path, raw, level, row.Count, children, leafRows)
            {
                Label = label,
                Aggregates = context.Aggregates is { } aggregates
                    ? await aggregates.ExecuteAsync(members, ct)
                    : GridAggregates.Empty,
            });
        }

        return (groups, total);
    }

    /// <summary>
    /// The column's selector as the group key. Strings group null and "" together (the in-memory
    /// grouper's blank bucket); other keys keep null as their own blank group.
    /// </summary>
    private static Expression<Func<T, TKey>> KeySelector<TKey>(LambdaExpression selector)
    {
        var body = selector.Body;
        if (typeof(TKey) == typeof(string))
        {
            body = Expression.Coalesce(body, Expression.Constant(string.Empty));
        }

        return Expression.Lambda<Func<T, TKey>>(body, selector.Parameters);
    }

    /// <summary>
    /// Blanks first when ascending and last when descending, like the in-memory grouper (the raw key
    /// of a blank is "", which sorts first). Without this PostgreSQL would put NULL last ascending.
    /// </summary>
    private static IOrderedQueryable<GroupKeyCount<TKey>> Order<TKey>(IQueryable<GroupKeyCount<TKey>> counts, bool descending)
    {
        var nullable = !typeof(TKey).IsValueType && typeof(TKey) != typeof(string) || Nullable.GetUnderlyingType(typeof(TKey)) is not null;
        if (!nullable)
        {
            return descending ? counts.OrderByDescending(static k => k.Key) : counts.OrderBy(static k => k.Key);
        }

        var item = Expression.Parameter(typeof(GroupKeyCount<TKey>), "k");
        var isBlank = Expression.Lambda<Func<GroupKeyCount<TKey>, bool>>(
            Expression.Equal(Expression.Property(item, nameof(GroupKeyCount<TKey>.Key)), Expression.Constant(null, typeof(TKey))),
            item);

        return descending
            ? counts.OrderBy(isBlank).ThenByDescending(static k => k.Key)
            : counts.OrderByDescending(isBlank).ThenBy(static k => k.Key);
    }

    /// <summary>
    /// <c>key(x) == value</c> with the value as a query parameter (a member of a captured holder, which
    /// EF turns into a parameter), so each group's query plan is reused.
    /// </summary>
    private static Expression<Func<T, bool>> KeyEquals<TKey>(Expression<Func<T, TKey>> key, TKey value)
    {
        var holder = Expression.Property(Expression.Constant(new KeyHolder<TKey>(value)), nameof(KeyHolder<TKey>.Value));
        return Expression.Lambda<Func<T, bool>>(Expression.Equal(key.Body, holder), key.Parameters);
    }

    private sealed record Context(
        GridQuery Query,
        IReadOnlyList<GridColumn<T>> Columns,
        HashSet<string> Expanded,
        Func<IQueryable<T>, IQueryable<T>> SortRows,
        EFGridAggregateProjection<T>? Aggregates,
        int MaxGroupRows,
        CancellationToken CancellationToken);
}

/// <summary>One row of a level's <c>GROUP BY</c>: the typed key and how many rows share it.</summary>
internal sealed class GroupKeyCount<TKey>
{
    public TKey Key { get; init; } = default!;
    public int Count { get; init; }
}

internal sealed class KeyHolder<TKey>(TKey value)
{
    public TKey Value { get; } = value;
}
