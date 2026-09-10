using System.Globalization;
using System.Text.Json;
using NetOpenGrid.Application.Binding;
using NetOpenGrid.Application.Engine;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain.Abstractions;
using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Domain.GridQuerying;
using NetOpenGrid.Domain.Results;

namespace NetOpenGrid.Infrastructure.Runtime;

/// <summary>
/// Per-grid runtime: parse → execute (async) → render. All heavy work happens on
/// precompiled delegates; the only async boundary is the data source.
/// </summary>
public sealed class GridRuntime<TItem> : IGridRuntime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly GridOptions<TItem> _options;
    private readonly IGridDataSource<TItem> _dataSource;
    private readonly Rendering.GridHtmlRenderer<TItem> _renderer;

    public GridRuntime(
        GridOptions<TItem> options,
        IGridDataSource<TItem> dataSource,
        NetOpenGridAssetOptions assetOptions,
        NetOpenGridLocalizationOptions localizationOptions)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(assetOptions);
        ArgumentNullException.ThrowIfNull(localizationOptions);
        _options = options;
        _dataSource = dataSource;
        _renderer = new Rendering.GridHtmlRenderer<TItem>(options, assetOptions, localizationOptions);
    }

    public string Id => _options.Id;

    public async ValueTask<GridRowsResponse> RenderRowsAsync(GridRequestValues values, CancellationToken cancellationToken = default)
    {
        var normalization = GridRequestParser.Parse(values, _options);
        var columnOrder = ResolveColumnOrder(values);

        if (normalization.Query.GroupFields.Count > 0 && _dataSource is IGridGroupingSource<TItem> groupingSource)
        {
            var grouped = await groupingSource.LoadGroupedAsync(normalization.Query, cancellationToken);
            var groupedHtml = await _renderer.RenderGroupedRowsAsync(grouped, columnOrder, cancellationToken);

            return new GridRowsResponse(
                groupedHtml,
                grouped.TotalGroups,
                grouped.Page,
                grouped.PageSize,
                Math.Max(grouped.TotalPages, 1));
        }

        var result = await new GridQueryEngine<TItem>(_dataSource).ExecuteAsync(normalization.Query, cancellationToken);
        var html = await _renderer.RenderRowsAsync(result, columnOrder, cancellationToken);

        return new GridRowsResponse(
            html,
            result.Page.TotalCount,
            result.Page.Page,
            result.Page.PageSize,
            Math.Max(result.Page.TotalPages, 1));
    }

    /// <summary>
    /// Client-driven column order ("cols=field1,field2"). Unknown, hidden or duplicate
    /// fields are ignored; visible columns missing from the list keep their default order at the end.
    /// </summary>
    private IReadOnlyList<GridColumn<TItem>>? ResolveColumnOrder(GridRequestValues values)
    {
        var raw = values.Get("cols");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var ordered = new List<GridColumn<TItem>>();
        foreach (var field in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (_options.TryGetColumn(field, out var column) &&
                column.IsVisible &&
                !ordered.Contains(column))
            {
                ordered.Add(column);
            }
        }

        foreach (var column in _options.Columns)
        {
            if (column.IsVisible && !ordered.Contains(column))
            {
                ordered.Add(column);
            }
        }

        return ordered;
    }

    /// <summary>Excel-style distinct value counts for one column, under the current query context.</summary>
    public async ValueTask<string> RenderValuesAsync(string field, GridRequestValues values, CancellationToken cancellationToken = default)
    {        var normalization = GridRequestParser.Parse(values, _options);

        if (!_options.TryGetColumn(field, out var column) || !column.IsFilterable)
        {
            return "{\"error\":\"unknown-field\"}";
        }

        if (_dataSource is not IGridValueCountSource<TItem> countingSource)
        {
            return "{\"values\":[],\"totalDistinct\":0,\"limit\":" +
                   _options.FilterValuesLimit.ToString(CultureInfo.InvariantCulture) + "}";
        }

        var counts = await countingSource.GetValuesAsync(column, normalization.Query, cancellationToken);

        var payload = JsonSerializer.Serialize(new
        {
            field,
            totalDistinct = counts.TotalDistinct,
            limit = _options.FilterValuesLimit,
            values = counts.Values
        }, JsonOptions);

        return payload;
    }

    /// <summary>
    /// Server-side CSV export of the FULL filtered+sorted dataset (all pages),
    /// rendered in the requested column order with the same cell formatters.
    /// </summary>
    public async ValueTask<(string FileName, string Csv)> RenderExportAsync(GridRequestValues values, CancellationToken cancellationToken = default)
    {
        var normalization = GridRequestParser.Parse(values, _options);
        var columns = ResolveColumnOrder(values)
            ?? _options.Columns.Where(static c => c.IsVisible).ToArray();

        var rows = new List<TItem>();
        var page = 1;

        while (true)
        {
            var pagedQuery = normalization.Query with
            {
                Paging = new PageRequest(page, Math.Max(_options.MaxPageSize, 1))
            };

            var result = await new GridQueryEngine<TItem>(_dataSource).ExecuteAsync(pagedQuery, cancellationToken);
            rows.AddRange(result.Page.Items);

            if (rows.Count >= result.Page.TotalCount || result.Page.Items.Count == 0)
            {
                break;
            }

            page++;
            cancellationToken.ThrowIfCancellationRequested();
        }

        return ($"{_options.Id}.csv", Export.GridCsvExporter.Build(columns, rows));
    }

    public async ValueTask<string> RenderShellAsync(GridRequestValues values, CancellationToken cancellationToken = default)
    {
        var normalization = GridRequestParser.Parse(values, _options);
        var result = await new GridQueryEngine<TItem>(_dataSource).ExecuteAsync(normalization.Query, cancellationToken);
        return await _renderer.RenderShellAsync(result, ResolveColumnOrder(values), cancellationToken);
    }

    public async ValueTask<string> RenderFragmentAsync(GridRequestValues values, CancellationToken cancellationToken = default)
    {
        var normalization = GridRequestParser.Parse(values, _options);
        var result = await new GridQueryEngine<TItem>(_dataSource).ExecuteAsync(normalization.Query, cancellationToken);
        return await _renderer.RenderFragmentAsync(result, ResolveColumnOrder(values), cancellationToken);
    }
}
