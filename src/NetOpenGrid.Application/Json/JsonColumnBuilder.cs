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
    private ColumnAlign _align = ColumnAlign.Start;
    private string? _widthCss;

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

    public JsonColumnBuilder Header(string header)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(header);
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
            DataType = ColumnDataType.Unknown,
            Align = _align,
            WidthCss = _widthCss
        };
    }
}
