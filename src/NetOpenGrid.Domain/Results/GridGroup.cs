using NetOpenGrid.Domain.GridQuerying;

namespace NetOpenGrid.Domain.Results;

/// <summary>
/// A grouping tree node: one distinct value of the level's group column.
/// <see cref="Children"/> is populated for expanded non-leaf levels; <see cref="Rows"/>
/// is populated only for expanded deepest-level groups.
/// </summary>
public sealed record GridGroup<T>(
    string Path,
    string Value,
    int Level,
    int Count,
    IReadOnlyList<GridGroup<T>> Children,
    IReadOnlyList<T> Rows)
{
    /// <summary>Subtotals over every row of the group (all of them, expanded or not).</summary>
    public GridAggregates Aggregates { get; init; } = GridAggregates.Empty;
}

/// <summary>Paged slice of top-level groups (grouping active: pages contain groups, not rows).</summary>
public sealed record GroupedPageResult<T>(
    IReadOnlyList<GridGroup<T>> Groups,
    int TotalGroups,
    int Page,
    int PageSize)
{
    public int TotalPages => PageSize < 1 ? 0 : (int)Math.Ceiling(TotalGroups / (double)PageSize);
}

/// <summary>Capability for sources able to serve grouped results (group → nested groups → rows).</summary>
public interface IGridGroupingSource<T>
{
    ValueTask<GroupedPageResult<T>> LoadGroupedAsync(GridQuery query, CancellationToken cancellationToken = default);
}
