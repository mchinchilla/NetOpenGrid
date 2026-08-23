namespace NetOpenGrid.Domain.GridQuerying;

public sealed record GridQuery(
    PageRequest Paging,
    IReadOnlyList<SortDescriptor> Sorts,
    IReadOnlyList<FilterDescriptor> Filters,
    string? Search = null)
{
    public static GridQuery Empty { get; } = new(
        new PageRequest(PageRequest.FirstPage, PageRequest.DefaultPageSize),
        [],
        [],
        null);
}
