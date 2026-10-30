namespace NetOpenGrid.Domain.Results;

public sealed record PageResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    /// <summary>
    /// True when <see cref="TotalCount"/> is a lower bound (the source stopped counting at its cap;
    /// <see cref="TotalCount"/> is then cap + 1). Default false.
    /// </summary>
    public bool TotalCountIsCapped { get; init; }

    /// <summary>
    /// With a capped count, "next" stays available while the current page is full, so paging can
    /// continue past the (unknown) real end.
    /// </summary>
    public int TotalPages
    {
        get
        {
            if (PageSize < 1) return 0;
            var pages = (int)Math.Ceiling(TotalCount / (double)PageSize);
            return TotalCountIsCapped && Items.Count >= PageSize ? Math.Max(pages, Page + 1) : pages;
        }
    }

    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;

    public static PageResult<T> Empty(int page, int pageSize) => new([], 0, page, pageSize);
}
