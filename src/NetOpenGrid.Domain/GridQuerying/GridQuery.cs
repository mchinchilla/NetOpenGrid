namespace NetOpenGrid.Domain.GridQuerying;

public sealed record GridQuery(
    PageRequest Paging,
    IReadOnlyList<SortDescriptor> Sorts,
    IReadOnlyList<FilterDescriptor> Filters,
    string? Search = null,
    IReadOnlyList<string>? GroupBy = null,
    IReadOnlyList<string>? ExpandedGroups = null,
    bool ExactCount = false)
{
    // ExactCount: ask the data source for the exact total even if it normally caps the count
    // (GridSqlMap.CountCap). Set by exports, which decide completeness and the row limit from it.
    public IReadOnlyList<string> GroupFields => GroupBy ?? [];

    public IReadOnlyList<string> Expanded => ExpandedGroups ?? [];

    public static GridQuery Empty { get; } = new(
        new PageRequest(PageRequest.FirstPage, PageRequest.DefaultPageSize),
        [],
        [],
        null);
}
