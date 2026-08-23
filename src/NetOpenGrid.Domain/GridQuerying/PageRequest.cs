namespace NetOpenGrid.Domain.GridQuerying;

public readonly record struct PageRequest(int Page = 1, int PageSize = 25)
{
    public const int DefaultPageSize = 25;
    public const int FirstPage = 1;

    public int Skip => (Math.Max(Page, FirstPage) - 1) * Math.Max(PageSize, 0);

    public PageRequest Normalized(int maxPageSize) => new(
        Math.Max(Page, FirstPage),
        Math.Clamp(PageSize, 1, Math.Max(maxPageSize, 1)));
}
