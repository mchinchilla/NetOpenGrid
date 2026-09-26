using System.Text.Json;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain;
using NetOpenGrid.Domain.Columns;

namespace NetOpenGrid.Application.Json;

/// <summary>
/// Fluent builder for <see cref="GridOptions{JsonElement}"/> (JSON mode).
/// Mirrors <see cref="Builders.GridOptionsBuilder{T}"/> semantics without typed selectors.
/// </summary>
public sealed class JsonGridOptionsBuilder
{
    private readonly List<GridColumn<JsonElement>> _columns = [];
    private readonly HashSet<string> _fields = new(StringComparer.Ordinal);
    private string? _id;
    private string _title = "Data grid";
    private string _subtitle = string.Empty;
    private int _defaultPageSize = 25;
    private int _maxPageSize = 500;
    private int[] _pageSizeChoices = [10, 25, 50, 100];
    private int _debounceMilliseconds = 300;
    private string _theme = "grid";
    private string _minHeight = "64rem";
    private int _filterValuesLimit = 200;
    private int _maxExportRows = 100_000;
    private GridExportFormats _exportFormats = GridExportFormats.All;
    private readonly List<(string Name, string Query)> _views = [];
    private bool _enableSavedViews = true;
    private bool _enableColumnResize = true;
    private bool _enableColumnChooser = true;
    private bool _enableGroupPanel = true;
    private GridVirtualScroll? _virtualScroll;
    private GridAggregateRows _aggregateRows = GridAggregateRows.Footer | GridAggregateRows.GroupHeader;
    private string _emptyMessage = "No records found.";
    private Func<JsonElement, string?>? _rowKey;
    private Func<JsonElement, string?>? _rowLink;
    private string? _rowLinkTarget;
    private IReadOnlyList<GridRowAction<JsonElement>> _rowActions = [];
    private string? _rowActionsHeader;
    private bool _rowActionsPinned;
    private bool _enableRowSelection;
    private List<GridNavLink> _navLinks = [];

    public JsonGridOptionsBuilder WithId(string id) { _id = id; return this; }
    public JsonGridOptionsBuilder WithTitle(string title) { _title = title; return this; }
    public JsonGridOptionsBuilder WithSubtitle(string subtitle) { _subtitle = subtitle; return this; }
    public JsonGridOptionsBuilder WithDefaultPageSize(int pageSize) { _defaultPageSize = pageSize; return this; }
    public JsonGridOptionsBuilder WithMaxPageSize(int maxPageSize) { _maxPageSize = maxPageSize; return this; }
    public JsonGridOptionsBuilder WithPageSizeChoices(params int[] choices) { _pageSizeChoices = choices; return this; }
    public JsonGridOptionsBuilder WithDebounce(int milliseconds) { _debounceMilliseconds = milliseconds; return this; }
    public JsonGridOptionsBuilder WithTheme(string themeName) { _theme = themeName; return this; }
    public JsonGridOptionsBuilder WithMinHeight(string minHeight) { _minHeight = minHeight?.Trim() ?? string.Empty; return this; }
    public JsonGridOptionsBuilder WithFilterValuesLimit(int limit) { _filterValuesLimit = Math.Max(limit, 1); return this; }
    /// <summary>Virtual scrolling instead of paging (see <c>GridOptionsBuilder.WithVirtualScroll</c>).</summary>
    public JsonGridOptionsBuilder WithVirtualScroll(int blockSize = 100, string height = "70vh")
    {
        if (blockSize is < 20 or > 1000)
        {
            throw new GridConfigurationException("Virtual scroll blockSize must be between 20 and 1000.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(height);
        _virtualScroll = new GridVirtualScroll(blockSize, height.Trim());
        _maxPageSize = Math.Max(_maxPageSize, blockSize);
        _defaultPageSize = blockSize;
        _pageSizeChoices = [blockSize];
        return this;
    }

    public JsonGridOptionsBuilder WithGroupPanel(bool enabled = true) { _enableGroupPanel = enabled; return this; }
    public JsonGridOptionsBuilder WithColumnChooser(bool enabled = true) { _enableColumnChooser = enabled; return this; }
    public JsonGridOptionsBuilder WithColumnResize(bool enabled = true) { _enableColumnResize = enabled; return this; }
    public JsonGridOptionsBuilder WithAggregateRows(GridAggregateRows rows) { _aggregateRows = rows; return this; }
    /// <summary>
    /// A predefined view every user sees, as the grid's own query string
    /// (e.g. <c>"filter=status:equals:delivered&amp;sort=total:desc"</c>). Validated at Build().
    /// </summary>
    public JsonGridOptionsBuilder AddView(string name, string query)
    {
        _views.Add((name, query));
        return this;
    }

    /// <summary>Lets users save their own views in the browser (on by default).</summary>
    public JsonGridOptionsBuilder WithSavedViews(bool enabled = true)
    {
        _enableSavedViews = enabled;
        return this;
    }

    private IReadOnlyList<GridSavedView> BuildViews()
    {
        var fields = new HashSet<string>(_columns.Select(static c => c.Field), StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return _views.Select(v => GridSavedViewValidator.Validate(v.Name, v.Query, fields, names)).ToArray();
    }

    public JsonGridOptionsBuilder WithExportFormats(GridExportFormats formats) { _exportFormats = formats; return this; }
    public JsonGridOptionsBuilder WithMaxExportRows(int maxRows) { _maxExportRows = Math.Max(maxRows, 1); return this; }
    public JsonGridOptionsBuilder WithEmptyMessage(string message) { _emptyMessage = message; return this; }
    public JsonGridOptionsBuilder WithNavLinks(params GridNavLink[] links) { _navLinks = [.. links]; return this; }

    /// <summary>Stable, unique key per row (used by row actions; EnableRowSelection sets it too).</summary>
    public JsonGridOptionsBuilder WithRowKey(Func<JsonElement, string?> rowKey)
    {
        ArgumentNullException.ThrowIfNull(rowKey);
        _rowKey = rowKey;
        return this;
    }

    /// <summary>
    /// Makes the whole row clickable. Clicks on the row's own controls, text selection and
    /// unsafe URL schemes are ignored; Ctrl/Cmd+click and middle click open a new tab.
    /// </summary>
    public JsonGridOptionsBuilder WithRowLink(Func<JsonElement, string?> href, string? target = null)
    {
        ArgumentNullException.ThrowIfNull(href);
        _rowLink = href;
        _rowLinkTarget = string.IsNullOrWhiteSpace(target) ? null : target;
        return this;
    }

    /// <summary>Trailing column of per-row links and event buttons.</summary>
    public JsonGridOptionsBuilder WithRowActions(Action<GridRowActionsBuilder<JsonElement>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new GridRowActionsBuilder<JsonElement>();
        configure(builder);
        _rowActions = builder.Build();
        _rowActionsHeader = builder.HeaderText;
        _rowActionsPinned = builder.IsPinned;
        return this;
    }

    public JsonGridOptionsBuilder EnableRowSelection(Func<JsonElement, string?> rowKey)
    {
        ArgumentNullException.ThrowIfNull(rowKey);
        _enableRowSelection = true;
        _rowKey = rowKey;
        return this;
    }

    public JsonGridOptionsBuilder AddColumn(string field, Action<JsonColumnBuilder>? configure = null)
    {
        var columnBuilder = new JsonColumnBuilder(field);
        configure?.Invoke(columnBuilder);

        if (!_fields.Add(columnBuilder.Field))
        {
            throw new GridConfigurationException($"Duplicate grid column field '{columnBuilder.Field}'.");
        }

        _columns.Add(columnBuilder.Build());
        return this;
    }

    public GridOptions<JsonElement> Build()
    {
        if (string.IsNullOrWhiteSpace(_id))
        {
            throw new GridConfigurationException("Grid id is required. Call WithId().");
        }

        if (_columns.Count == 0)
        {
            throw new GridConfigurationException("At least one column is required.");
        }

        if (_enableRowSelection && _rowKey is null)
        {
            throw new GridConfigurationException("EnableRowSelection requires a RowKey function.");
        }

        if (_rowActions.Any(static a => a.Kind == RowActionKind.Event) && _rowKey is null)
        {
            throw new GridConfigurationException(
                "Row actions of kind Event need a row key: call WithRowKey(...) or EnableRowSelection(...).");
        }

        if (_pageSizeChoices.Length == 0 || _pageSizeChoices.Any(static c => c is < 1))
        {
            throw new GridConfigurationException("Invalid page size choices.");
        }

        return new GridOptions<JsonElement>
        {
            Id = _id,
            Title = _title,
            Subtitle = _subtitle,
            Columns = _columns.ToArray(),
            DefaultPageSize = Math.Clamp(_defaultPageSize, 1, Math.Max(_maxPageSize, 1)),
            MaxPageSize = Math.Max(_maxPageSize, 1),
            PageSizeChoices = _pageSizeChoices.Distinct().Order().ToArray(),
            DebounceMilliseconds = _debounceMilliseconds,
            Theme = _theme,
            MinHeight = _minHeight,
            FilterValuesLimit = _filterValuesLimit,
            MaxExportRows = _maxExportRows,
            ExportFormats = _exportFormats,
            Views = BuildViews(),
            EnableSavedViews = _enableSavedViews,
            EnableColumnResize = _enableColumnResize,
            EnableColumnChooser = _enableColumnChooser,
            EnableGroupPanel = _enableGroupPanel,
            VirtualScroll = _virtualScroll,
            AggregateRows = _aggregateRows,
            EmptyMessage = _emptyMessage,
            EnableRowSelection = _enableRowSelection,
            RowKey = _rowKey,
            RowLink = _rowLink,
            RowLinkTarget = _rowLinkTarget,
            RowActions = _rowActions,
            RowActionsHeader = _rowActionsHeader,
            RowActionsPinned = _rowActionsPinned,
            NavLinks = _navLinks.ToArray()
        };
    }
}
