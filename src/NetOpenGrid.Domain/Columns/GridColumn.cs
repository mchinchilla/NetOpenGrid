using System.Linq.Expressions;
using NetOpenGrid.Domain.Abstractions;
using NetOpenGrid.Domain.GridQuerying;

namespace NetOpenGrid.Domain.Columns;

/// <summary>
/// Immutable column definition. All accessors are precompiled delegates captured at
/// options-build time; the request pipeline never uses reflection.
/// </summary>
public sealed class GridColumn<T>
{
    public required string Field { get; init; }
    public required string Header { get; init; }
    public required Func<T, string?> Format { get; init; }

    /// <summary>When set, its output is emitted as trusted raw HTML (server-controlled only).</summary>
    public Func<T, string?>? RawCellHtml { get; init; }

    /// <summary>
    /// The original selector expression (kept when defined via the Expression overloads).
    /// Required by SQL push-down sources such as <c>EFCoreGridDataSource&lt;T&gt;</c>.
    /// </summary>
    public LambdaExpression? SelectorExpression { get; init; }

    /// <summary>
    /// Non-generic filter-literal parser (built once, TKey known at build time).
    /// Used by SQL push-down sources to embed parsed constants into expression trees.
    /// </summary>
    public GridFilterValueParser? FilterValueParser { get; init; }

    /// <summary>Raw (parseable-back) display of the column key for value-count lists; in-memory grouping.</summary>
    public Func<T, string?>? RawKeyFormat { get; init; }

    /// <summary>Grouped key (boxed) → raw display string; EF value-count lists.</summary>
    public Func<object?, string?>? KeyFormatter { get; init; }

    /// <summary>Pinned columns stay visible during horizontal scroll (CSS sticky, left edge).</summary>
    public bool IsPinned { get; init; }

    public ISortStrategy<T>? SortStrategy { get; init; }
    public IFilterStrategyFactory<T>? FilterFactory { get; init; }
    public ISearchStrategy<T>? SearchStrategy { get; init; }

    public FilterOpSet AllowedOps { get; init; } = FilterOpSet.None;
    public bool IsSortable { get; init; }
    public bool IsFilterable => FilterFactory is not null && AllowedOps != FilterOpSet.None;
    public bool IsSearchable { get; init; }
    public bool IsVisible { get; init; } = true;

    public ColumnDataType DataType { get; init; } = ColumnDataType.Unknown;
    public ColumnAlign Align { get; init; } = ColumnAlign.Start;
    public string? WidthCss { get; init; }
}
