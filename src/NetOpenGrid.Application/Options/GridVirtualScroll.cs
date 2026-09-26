namespace NetOpenGrid.Application.Options;

/// <summary>
/// Virtual scrolling: the pager is replaced by a scrollable viewport of <see cref="Height"/> and
/// rows arrive from the server in blocks of <see cref="BlockSize"/> as the user scrolls; only the
/// blocks near the viewport stay in the DOM. Grouped views keep paging.
/// </summary>
public sealed record GridVirtualScroll(int BlockSize, string Height);
