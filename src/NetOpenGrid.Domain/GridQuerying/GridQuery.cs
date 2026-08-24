namespace NetOpenGrid.Domain.GridQuerying;

public sealed record GridQuery(
    PageRequest Paging,
    IReadOnlyList<SortDescriptor> Sorts,
    IReadOnlyList<FilterDescriptor> Filters,
    string? Search = null,
    IReadOnlyList<string>? GroupBy = null,
    IReadOnlyList<string>? ExpandedGroups = null)
{
    public IReadOnlyList<string> GroupFields => GroupBy ?? [];

    public IReadOnlyList<string> Expanded => ExpandedGroups ?? [];

    public static GridQuery Empty { get; } = new(
        new PageRequest(PageRequest.FirstPage, PageRequest.DefaultPageSize),
        [],
        [],
        null);
}
