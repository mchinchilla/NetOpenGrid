using System.Linq.Expressions;
using System.Text.RegularExpressions;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain;
using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Domain.GridQuerying;

namespace NetOpenGrid.Application.Builders;

/// <summary>
/// Fluent builder producing an immutable <see cref="GridOptions{T}"/>.
/// Validation happens once at Build(); the request path never re-validates.
/// </summary>
public sealed class GridOptionsBuilder<T>
{
    private readonly List<GridColumn<T>> _columns = [];
    private readonly HashSet<string> _fields = new(StringComparer.Ordinal);
    private string? _id;
    private string _title = "Data grid";
    private string _subtitle = string.Empty;
    private int _defaultPageSize = PageRequest.DefaultPageSize;
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
    private Func<T, string?>? _rowKey;
    private bool _enableRowSelection;
    private Func<T, string?>? _rowLink;
    private string? _rowLinkTarget;
    private IReadOnlyList<GridRowAction<T>> _rowActions = [];
    private string? _rowActionsHeader;
    private bool _rowActionsPinned;
    private List<GridNavLink> _navLinks = [];

    public GridOptionsBuilder<T> WithId(string id)
    {
        _id = id;
        return this;
    }

    public GridOptionsBuilder<T> WithTitle(string title)
    {
        _title = title;
        return this;
    }

    public GridOptionsBuilder<T> WithSubtitle(string subtitle)
    {
        _subtitle = subtitle;
        return this;
    }

    public GridOptionsBuilder<T> WithDefaultPageSize(int pageSize)
    {
        _defaultPageSize = pageSize;
        return this;
    }

    public GridOptionsBuilder<T> WithMaxPageSize(int maxPageSize)
    {
        _maxPageSize = maxPageSize;
        return this;
    }

    public GridOptionsBuilder<T> WithPageSizeChoices(params int[] choices)
    {
        _pageSizeChoices = choices;
        return this;
    }

    public GridOptionsBuilder<T> WithDebounce(int milliseconds)
    {
        _debounceMilliseconds = milliseconds;
        return this;
    }

    public GridOptionsBuilder<T> WithTheme(string themeName)
    {
        _theme = themeName;
        return this;
    }

    /// <summary>Minimum table-card height (CSS value, default ≈25 rows). Pass "" to disable.</summary>
    public GridOptionsBuilder<T> WithMinHeight(string minHeight)
    {
        _minHeight = minHeight?.Trim() ?? string.Empty;
        return this;
    }

    /// <summary>Max distinct values returned by Excel-style value-count lists.</summary>
    public GridOptionsBuilder<T> WithFilterValuesLimit(int limit)
    {
        _filterValuesLimit = Math.Max(limit, 1);
        return this;
    }

    /// <summary>
    /// Replaces the pager with virtual scrolling: a viewport of <paramref name="height"/> (CSS) that
    /// loads rows in blocks of <paramref name="blockSize"/> while scrolling. The page size becomes the
    /// block size (and MaxPageSize grows to fit it). Grouped views keep paging.
    /// </summary>
    public GridOptionsBuilder<T> WithVirtualScroll(int blockSize = 100, string height = "70vh")
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

    /// <summary>Group panel above the table (on by default); off falls back to plain group chips.</summary>
    public GridOptionsBuilder<T> WithGroupPanel(bool enabled = true)
    {
        _enableGroupPanel = enabled;
        return this;
    }

    /// <summary>Toolbar menu to show/hide columns (on by default).</summary>
    public GridOptionsBuilder<T> WithColumnChooser(bool enabled = true)
    {
        _enableColumnChooser = enabled;
        return this;
    }

    /// <summary>Column resizing from the header edge (on by default; widths persist per browser).</summary>
    public GridOptionsBuilder<T> WithColumnResize(bool enabled = true)
    {
        _enableColumnResize = enabled;
        return this;
    }

    /// <summary>Where aggregate rows are rendered (default: table footer + group headers).</summary>
    public GridOptionsBuilder<T> WithAggregateRows(GridAggregateRows rows)
    {
        _aggregateRows = rows;
        return this;
    }

    /// <summary>
    /// A predefined view every user sees, as the grid's own query string
    /// (e.g. <c>"filter=status:equals:delivered&amp;sort=total:desc"</c>). Validated at Build().
    /// </summary>
    public GridOptionsBuilder<T> AddView(string name, string query)
    {
        _views.Add((name, query));
        return this;
    }

    /// <summary>Lets users save their own views in the browser (on by default).</summary>
    public GridOptionsBuilder<T> WithSavedViews(bool enabled = true)
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

    /// <summary>Export formats offered (default CSV and xlsx); <see cref="GridExportFormats.None"/> removes the export.</summary>
    public GridOptionsBuilder<T> WithExportFormats(GridExportFormats formats)
    {
        _exportFormats = formats;
        return this;
    }

    /// <summary>Row cap for the built-in CSV export; larger result sets are rejected up front.</summary>
    public GridOptionsBuilder<T> WithMaxExportRows(int maxRows)
    {
        _maxExportRows = Math.Max(maxRows, 1);
        return this;
    }

    public GridOptionsBuilder<T> WithEmptyMessage(string message)
    {
        _emptyMessage = message;
        return this;
    }

    /// <summary>Stable, unique key per row (used by row actions; EnableRowSelection sets it too).</summary>
    public GridOptionsBuilder<T> WithRowKey(Func<T, string?> rowKey)
    {
        ArgumentNullException.ThrowIfNull(rowKey);
        _rowKey = rowKey;
        return this;
    }

    /// <summary>
    /// Makes the whole row clickable. Clicks on the row's own controls, text selection and
    /// unsafe URL schemes are ignored; Ctrl/Cmd+click and middle click open a new tab.
    /// </summary>
    public GridOptionsBuilder<T> WithRowLink(Func<T, string?> href, string? target = null)
    {
        ArgumentNullException.ThrowIfNull(href);
        _rowLink = href;
        _rowLinkTarget = string.IsNullOrWhiteSpace(target) ? null : target;
        return this;
    }

    /// <summary>Trailing column of per-row links and event buttons.</summary>
    public GridOptionsBuilder<T> WithRowActions(Action<GridRowActionsBuilder<T>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new GridRowActionsBuilder<T>();
        configure(builder);
        _rowActions = builder.Build();
        _rowActionsHeader = builder.HeaderText;
        _rowActionsPinned = builder.IsPinned;
        return this;
    }

    /// <summary>Enables client-side row selection. Keys must be stable and unique.</summary>
    public GridOptionsBuilder<T> EnableRowSelection(Func<T, string?> rowKey)
    {
        ArgumentNullException.ThrowIfNull(rowKey);
        _enableRowSelection = true;
        _rowKey = rowKey;
        return this;
    }

    public GridOptionsBuilder<T> WithNavLinks(params GridNavLink[] links)
    {
        _navLinks = [.. links];
        return this;
    }

    public GridOptionsBuilder<T> AddColumn<TKey>(
        string field,
        Func<T, TKey> selector,
        Action<GridColumnBuilder<T, TKey>>? configure = null)
    {
        var columnBuilder = new GridColumnBuilder<T, TKey>(field, selector);
        configure?.Invoke(columnBuilder);

        if (!_fields.Add(columnBuilder.Field))
        {
            throw new GridConfigurationException($"Duplicate grid column field '{columnBuilder.Field}'.");
        }

        _columns.Add(columnBuilder.Build());
        return this;
    }

    /// <summary>Adds a column deriving the field name from the member expression (startup-time sugar).</summary>
    public GridOptionsBuilder<T> AddColumn<TKey>(
        Expression<Func<T, TKey>> selector,
        Action<GridColumnBuilder<T, TKey>>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(selector);

        var rawName = ExtractMemberName(selector);
        var columnBuilder = new GridColumnBuilder<T, TKey>(Camelize(rawName), selector.Compile(), selector);
        columnBuilder.SetDefaultHeader(ColumnNameHumanizer.Humanize(rawName));
        configure?.Invoke(columnBuilder);

        if (!_fields.Add(columnBuilder.Field))
        {
            throw new GridConfigurationException($"Duplicate grid column field '{columnBuilder.Field}'.");
        }

        _columns.Add(columnBuilder.Build());
        return this;
    }

    public GridOptions<T> Build()
    {
        ValidateId();
        ValidatePaging();
        ValidateColumns();

        return new GridOptions<T>
        {
            Id = _id!,
            Title = _title,
            Subtitle = _subtitle,
            Columns = _columns.ToArray(),
            DefaultPageSize = _defaultPageSize,
            MaxPageSize = _maxPageSize,
            PageSizeChoices = _pageSizeChoices,
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

    private void ValidateId()
    {
        if (string.IsNullOrWhiteSpace(_id))
        {
            throw new GridConfigurationException("Grid id is required. Call WithId().");
        }

        if (!IdPattern.IsMatch(_id))
        {
            throw new GridConfigurationException(
                $"Grid id '{_id}' is invalid. Use 1-64 chars: letters, digits, '-' or '_'.");
        }
    }

    private void ValidatePaging()
    {
        if (_maxPageSize < 1)
        {
            throw new GridConfigurationException("MaxPageSize must be >= 1.");
        }

        if (_defaultPageSize is < 1 || _defaultPageSize > _maxPageSize)
        {
            throw new GridConfigurationException(
                $"DefaultPageSize ({_defaultPageSize}) must be between 1 and MaxPageSize ({_maxPageSize}).");
        }

        if (_pageSizeChoices.Length == 0)
        {
            throw new GridConfigurationException("At least one page size choice is required.");
        }

        if (_pageSizeChoices.Any(c => c is < 1 || c > _maxPageSize))
        {
            throw new GridConfigurationException($"All page size choices must be between 1 and MaxPageSize ({_maxPageSize}).");
        }

        _pageSizeChoices = _pageSizeChoices.Distinct().Order().ToArray();
    }

    private void ValidateColumns()
    {
        if (_columns.Count == 0)
        {
            throw new GridConfigurationException("At least one column is required.");
        }

        if (_columns.All(static c => !c.IsVisible))
        {
            throw new GridConfigurationException("At least one visible column is required.");
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
    }

    private static string ExtractMemberName<TKey>(Expression<Func<T, TKey>> selector)
    {
        var body = selector.Body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary
            ? unary.Operand
            : selector.Body;

        return body is MemberExpression member
            ? member.Member.Name
            : throw new GridConfigurationException(
                "Expression selectors must be simple member access (e.g. e => e.Name). " +
                "Use the (field, selector) overload for computed columns.");
    }

    private static string Camelize(string name) =>
        name.Length > 0 && char.IsUpper(name[0])
            ? char.ToLowerInvariant(name[0]) + name[1..]
            : name;

    private static readonly Regex IdPattern = new("^[A-Za-z][A-Za-z0-9_-]{0,63}$", RegexOptions.Compiled);
}
