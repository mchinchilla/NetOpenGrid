using System.Globalization;
using System.Text.Json;
using NetOpenGrid.Application.Binding;
using NetOpenGrid.Application.Engine;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain.Abstractions;

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

    public GridRuntime(GridOptions<TItem> options, IGridDataSource<TItem> dataSource, NetOpenGridAssetOptions assetOptions)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(assetOptions);
        _options = options;
        _dataSource = dataSource;
        _renderer = new Rendering.GridHtmlRenderer<TItem>(options, assetOptions);
    }

    public string Id => _options.Id;

    public async ValueTask<GridRowsResponse> RenderRowsAsync(GridRequestValues values, CancellationToken cancellationToken = default)
    {
        var normalization = GridRequestParser.Parse(values, _options);
        var result = await new GridQueryEngine<TItem>(_dataSource).ExecuteAsync(normalization.Query, cancellationToken);
        var html = await _renderer.RenderRowsAsync(result, cancellationToken);

        return new GridRowsResponse(
            html,
            result.Page.TotalCount,
            result.Page.Page,
            result.Page.PageSize,
            Math.Max(result.Page.TotalPages, 1));
    }

    /// <summary>Excel-style distinct value counts for one column, under the current query context.</summary>
    public async ValueTask<string> RenderValuesAsync(string field, GridRequestValues values, CancellationToken cancellationToken = default)
    {
        var normalization = GridRequestParser.Parse(values, _options);

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

    public async ValueTask<string> RenderShellAsync(GridRequestValues values, CancellationToken cancellationToken = default)
    {
        var normalization = GridRequestParser.Parse(values, _options);
        var result = await new GridQueryEngine<TItem>(_dataSource).ExecuteAsync(normalization.Query, cancellationToken);
        return await _renderer.RenderShellAsync(result, cancellationToken);
    }
}
