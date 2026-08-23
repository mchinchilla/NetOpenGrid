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
    private string _emptyMessage = "No records found.";
    private Func<JsonElement, string?>? _rowKey;
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
    public JsonGridOptionsBuilder WithEmptyMessage(string message) { _emptyMessage = message; return this; }
    public JsonGridOptionsBuilder WithNavLinks(params GridNavLink[] links) { _navLinks = [.. links]; return this; }

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
            EmptyMessage = _emptyMessage,
            EnableRowSelection = _enableRowSelection,
            RowKey = _rowKey,
            NavLinks = _navLinks.ToArray()
        };
    }
}
