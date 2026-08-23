namespace NetOpenGrid.Domain.Results;

public sealed record PageResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => PageSize < 1 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;

    public static PageResult<T> Empty(int page, int pageSize) => new([], 0, page, pageSize);
}
