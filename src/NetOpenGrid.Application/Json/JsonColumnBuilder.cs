using System.Globalization;
using System.Text.Json;
using NetOpenGrid.Domain;
using NetOpenGrid.Domain.Abstractions;
using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Domain.GridQuerying;

namespace NetOpenGrid.Application.Json;

/// <summary>
/// Fluent builder for columns over <see cref="JsonElement"/> rows.
/// Field names are plain JSON property names; no reflection is involved anywhere.
/// </summary>
public sealed class JsonColumnBuilder
{
    private readonly string _field;
    private string? _header;
    private bool _sortable = true;
    private bool _filterable = true;
    private FilterOpSet _allowedOps = FilterOpSet.Text | FilterOpSet.Numeric;
    private bool? _searchable;
    private Func<JsonElement, string?>? _rawCellHtml;
    private bool _visible = true;
    private bool _pinned;
    private bool _pinnedRight;
    private ColumnAlign _align = ColumnAlign.Start;
    private string? _widthCss;
    private GridAggregate _aggregates = GridAggregate.None;
    private Func<GridAggregate, decimal, string?>? _aggregateFormat;
    private string? _excelFormat;

    internal JsonColumnBuilder(string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        if (field.Contains(':'))
        {
            throw new GridException($"JSON field '{field}' cannot contain ':'.");
        }

        _field = field;
    }

    public string Field => _field;

    /// <summary>Empty is allowed: see <c>GridColumnBuilder.Header</c>.</summary>
    public JsonColumnBuilder Header(string header)
    {
        ArgumentNullException.ThrowIfNull(header);
        _header = header;
        return this;
    }

    public JsonColumnBuilder Sortable(bool sortable = true)
    {
        _sortable = sortable;
        return this;
    }

    public JsonColumnBuilder Filterable(bool filterable = true)
    {
        _filterable = filterable;
        return this;
    }

    public JsonColumnBuilder AllowedOps(FilterOpSet ops)
    {
        _allowedOps = ops;
        return this;
    }

    public JsonColumnBuilder Searchable(bool searchable = true)
    {
        _searchable = searchable;
        return this;
    }

    public JsonColumnBuilder RawCellHtml(Func<JsonElement, string?> renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _rawCellHtml = renderer;
        return this;
    }

    public JsonColumnBuilder Visible(bool visible = true)
    {
        _visible = visible;
        return this;
    }

    /// <summary>Pins the column to the left edge.</summary>
    public JsonColumnBuilder Pinned(bool pinned = true)
    {
        _pinned = pinned;
        if (pinned) _pinnedRight = false;
        return this;
    }

    /// <summary>Pins the column to the right edge; right-pinned columns are shown last.</summary>
    public JsonColumnBuilder PinnedRight(bool pinned = true)
    {
        _pinnedRight = pinned;
        if (pinned) _pinned = false;
        return this;
    }

    public JsonColumnBuilder Align(ColumnAlign align)
    {
        _align = align;
        return this;
    }

    public JsonColumnBuilder WidthCss(string css)
    {
        _widthCss = css;
        return this;
    }

    /// <summary>Aggregate functions over the property's numeric values (non-numbers are skipped).</summary>
    public JsonColumnBuilder Aggregate(GridAggregate functions)
    {
        _aggregates = functions;
        return this;
    }

    public JsonColumnBuilder AggregateFormat(Func<GridAggregate, decimal, string?> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        _aggregateFormat = formatter;
        return this;
    }

    /// <summary>Excel number format for xlsx exports.</summary>
    public JsonColumnBuilder ExcelFormat(string numberFormat)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(numberFormat);
        _excelFormat = numberFormat;
        return this;
    }

    internal GridColumn<JsonElement> Build()
    {
        var supported = FilterOpSet.None;
        IFilterStrategyFactory<JsonElement>? filterFactory = null;

        if (_filterable)
        {
            var ops = _allowedOps & (FilterOpSet.Text | FilterOpSet.Numeric | FilterOpSet.IsEmpty | FilterOpSet.IsNotEmpty);
            if (ops != FilterOpSet.None)
            {
                filterFactory = new JsonStrategies.FilterStrategyFactory(_field, ops);
                supported = ops;
            }
        }

        var searchStrategy = _searchable == true
            ? new JsonStrategies.SearchStrategy(_field)
            : null;

        return new GridColumn<JsonElement>
        {
            Field = _field,
            Header = _header ?? Builders.ColumnNameHumanizer.Humanize(_field),
            Label = string.IsNullOrWhiteSpace(_header) ? Builders.ColumnNameHumanizer.Humanize(_field) : _header,
            Format = element =>
                JsonStrategies.TryGetProperty(element, _field, out var value)
                    ? JsonStrategies.Format(value)
                    : null,
            RawKeyFormat = element =>
                JsonStrategies.TryGetProperty(element, _field, out var keyValue)
                    ? JsonStrategies.Format(keyValue)
                    : null,
            KeyFormatter = boxed => boxed is JsonElement jsonElement ? JsonStrategies.Format(jsonElement) : null,
            RawCellHtml = _rawCellHtml,
            SortStrategy = _sortable ? new JsonStrategies.SortStrategy(_field) : null,
            FilterFactory = filterFactory,
            SearchStrategy = searchStrategy,
            AllowedOps = supported,
            IsSortable = _sortable,
            IsSearchable = searchStrategy is not null,
            IsVisible = _visible,
            IsPinned = _pinned,
            IsPinnedRight = _pinnedRight,
            DataType = ColumnDataType.Unknown,
            Align = _align,
            WidthCss = _widthCss,
            RawValue = element => JsonStrategies.TryGetProperty(element, _field, out var raw) ? raw : null,
            ExcelFormat = _excelFormat,
            Aggregates = _aggregates,
            AggregateValue = _aggregates == GridAggregate.None
                ? null
                : element => JsonStrategies.TryGetProperty(element, _field, out var number) &&
                             number.ValueKind == JsonValueKind.Number &&
                             number.TryGetDecimal(out var parsed)
                    ? parsed
                    : null,
            AggregateFormat = _aggregates == GridAggregate.None
                ? null
                : _aggregateFormat ?? (static (_, value) => Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture))
        };
    }
}
