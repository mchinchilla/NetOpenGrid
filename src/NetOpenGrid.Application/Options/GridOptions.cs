using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Domain.GridQuerying;

namespace NetOpenGrid.Application.Options;

public sealed record GridNavLink(string Label, string Href);

public sealed class GridOptions<T>
{
    public required string Id { get; init; }
    public required IReadOnlyList<GridColumn<T>> Columns { get; init; }
    public string Title { get; init; } = "Data grid";
    public string Subtitle { get; init; } = string.Empty;
    public int DefaultPageSize { get; init; } = PageRequest.DefaultPageSize;
    public int MaxPageSize { get; init; } = 500;
    public IReadOnlyList<int> PageSizeChoices { get; init; } = [10, 25, 50, 100];
    public int DebounceMilliseconds { get; init; } = 300;
    public string Theme { get; init; } = "grid";

    /// <summary>Minimum table-card height (CSS value). Keeps the grid from collapsing
    /// when few rows match; default ≈ 25 rows. Empty string disables it.</summary>
    public string MinHeight { get; init; } = "64rem";

    /// <summary>Max distinct values returned by Excel-style value-count lists.</summary>
    public int FilterValuesLimit { get; init; } = 200;

    /// <summary>Server-defined views every user sees in the views menu.</summary>
    public IReadOnlyList<GridSavedView> Views { get; init; } = [];

    /// <summary>Lets users save their own named views (kept in their browser).</summary>
    public bool EnableSavedViews { get; init; } = true;

    /// <summary>Formats offered by the export button and endpoint.</summary>
    public GridExportFormats ExportFormats { get; init; } = GridExportFormats.All;

    /// <summary>Row cap for the built-in CSV export; larger result sets are rejected up front.</summary>
    public int MaxExportRows { get; init; } = 100_000;

    /// <summary>Where aggregate rows are rendered; only columns with <c>Aggregate(...)</c> show values.</summary>
    public GridAggregateRows AggregateRows { get; init; } = GridAggregateRows.Footer | GridAggregateRows.GroupHeader;

    /// <summary>Virtual scrolling instead of paging (null: classic pager).</summary>
    public GridVirtualScroll? VirtualScroll { get; init; }

    /// <summary>Drop zone above the table: drag a column header in to group by it, drag chips to reorder levels.</summary>
    public bool EnableGroupPanel { get; init; } = true;

    /// <summary>Toolbar menu to show/hide columns (choice persisted per browser and in the URL).</summary>
    public bool EnableColumnChooser { get; init; } = true;

    /// <summary>Drag handles on the column headers (widths persisted per browser).</summary>
    public bool EnableColumnResize { get; init; } = true;

    public string EmptyMessage { get; init; } = "No records found.";
    public bool EnableRowSelection { get; init; }
    public Func<T, string?>? RowKey { get; init; }

    /// <summary>Whole-row link: clicking the row (outside its controls) navigates here.</summary>
    public Func<T, string?>? RowLink { get; init; }

    /// <summary>Browsing context for <see cref="RowLink"/> (e.g. "_top" out of an iframe, "_blank").</summary>
    public string? RowLinkTarget { get; init; }

    /// <summary>Links/buttons rendered in a trailing actions column.</summary>
    public IReadOnlyList<GridRowAction<T>> RowActions { get; init; } = [];

    /// <summary>Keeps the actions column stuck to the right edge during horizontal scroll.</summary>
    public bool RowActionsPinned { get; init; }

    /// <summary>Header of the actions column; null uses the localized "Actions".</summary>
    public string? RowActionsHeader { get; init; }

    public bool HasRowActions => RowActions.Count > 0;
    public IReadOnlyList<GridNavLink> NavLinks { get; init; } = [];

    private Dictionary<string, GridColumn<T>>? _columnIndex;
    private GridColumn<T>[]? _aggregateColumns;
    private GridColumn<T>[]? _displayColumns;

    /// <summary>
    /// Visible columns in their default display order: left-pinned first, then the rest, then
    /// right-pinned (each group keeps the configured order). Pinned columns must sit at their edge
    /// for CSS sticky to keep them in view.
    /// </summary>
    public IReadOnlyList<GridColumn<T>> DisplayColumns => _displayColumns ??=
    [
        .. Columns.Where(static c => c.IsVisible && c.IsPinned),
        .. Columns.Where(static c => c.IsVisible && !c.IsPinned && !c.IsPinnedRight),
        .. Columns.Where(static c => c.IsVisible && c.IsPinnedRight)
    ];

    /// <summary>Visible columns that declare at least one aggregate function.</summary>
    public IReadOnlyList<GridColumn<T>> AggregateColumns =>
        _aggregateColumns ??= Columns.Where(static c => c.IsVisible && c.HasAggregates).ToArray();

    public bool ShowsAggregates(GridAggregateRows rows) => (AggregateRows & rows) != 0 && AggregateColumns.Count > 0;

    public bool TryGetColumn(string? field, out GridColumn<T> column)
    {
        if (field is null)
        {
            column = null!;
            return false;
        }

        _columnIndex ??= Columns.ToDictionary(static c => c.Field, static c => c, StringComparer.Ordinal);
        return _columnIndex.TryGetValue(field, out column!);
    }
}
