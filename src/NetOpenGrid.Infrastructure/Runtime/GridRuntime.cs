using System.Globalization;
using System.Text.Json;
using NetOpenGrid.Application.Binding;
using NetOpenGrid.Application.Engine;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain;
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

    public GridExportFormats ExportFormats => _options.ExportFormats;

    public async ValueTask<GridRowsResponse> RenderRowsAsync(GridRequestValues values, CancellationToken cancellationToken = default)
    {
        var normalization = GridRequestParser.Parse(values, _options);
        var columnOrder = ResolveColumnOrder(values);
        var hidden = ResolveHiddenFields(values);

        if (normalization.Query.GroupFields.Count > 0 && _dataSource is IGridGroupingSource<TItem> groupingSource)
        {
            var grouped = await groupingSource.LoadGroupedAsync(normalization.Query, cancellationToken);
            var groupTotals = grouped.TotalGroups > 0 ? await LoadTotalsAsync(normalization.Query, cancellationToken) : null;
            var groupedHtml = await _renderer.RenderGroupedRowsAsync(grouped, columnOrder, cancellationToken, groupTotals, hidden);

            return new GridRowsResponse(
                groupedHtml,
                grouped.TotalGroups,
                grouped.Page,
                grouped.PageSize,
                Math.Max(grouped.TotalPages, 1));
        }

        var result = await new GridQueryEngine<TItem>(_dataSource).ExecuteAsync(normalization.Query, cancellationToken);
        var totals = await LoadTotalsAsync(result, cancellationToken);
        var html = await _renderer.RenderRowsAsync(result, columnOrder, cancellationToken, totals, hidden);

        return new GridRowsResponse(
            html,
            result.Page.TotalCount,
            result.Page.Page,
            result.Page.PageSize,
            Math.Max(result.Page.TotalPages, 1));
    }

    /// <summary>
    /// Grand totals for the table header/footer rows, only when the grid shows them, the source
    /// can compute them and there is at least one row (an empty grid renders no totals row).
    /// </summary>
    private ValueTask<GridAggregates?> LoadTotalsAsync(GridExecutionResult<TItem> result, CancellationToken cancellationToken) =>
        result.Page.TotalCount > 0
            ? LoadTotalsAsync(result.Query, cancellationToken)
            : ValueTask.FromResult<GridAggregates?>(null);

    private async ValueTask<GridAggregates?> LoadTotalsAsync(GridQuery query, CancellationToken cancellationToken) =>
        _options.ShowsAggregates(GridAggregateRows.Header | GridAggregateRows.Footer) &&
        _dataSource is IGridAggregateSource<TItem> source
            ? await source.GetAggregatesAsync(query, cancellationToken)
            : null;

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

        foreach (var column in _options.DisplayColumns)
        {
            if (column.IsVisible && !ordered.Contains(column))
            {
                ordered.Add(column);
            }
        }

        return ordered;
    }

    /// <summary>
    /// Columns the user hid ("hide=field1,field2"): only known visible fields count, and a request
    /// that would hide every column is ignored rather than rendering an empty table.
    /// </summary>
    private IReadOnlySet<string>? ResolveHiddenFields(GridRequestValues values)
    {
        var raw = values.Get("hide");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var hidden = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (_options.TryGetColumn(field, out var column) && column.IsVisible)
            {
                hidden.Add(field);
            }
        }

        var visibleCount = _options.Columns.Count(static c => c.IsVisible);
        return hidden.Count == 0 || hidden.Count >= visibleCount ? null : hidden;
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
    /// Server-side export (CSV or xlsx) of the FULL filtered+sorted dataset (all pages), in the
    /// requested column order and without the hidden columns. Only the first page is loaded here
    /// (to know the total); the rest is fetched page by page while <see cref="GridExport.WriteToAsync"/>
    /// streams, so memory stays bounded by <c>MaxPageSize</c> rows.
    /// </summary>
    public async ValueTask<GridExport> CreateExportAsync(
        GridRequestValues values,
        GridExportFormats format = GridExportFormats.Csv,
        CancellationToken cancellationToken = default)
    {
        if (format is not (GridExportFormats.Csv or GridExportFormats.Xlsx))
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "Pick a single export format.");
        }

        var normalization = GridRequestParser.Parse(values, _options);
        var hidden = ResolveHiddenFields(values);
        IReadOnlyList<GridColumn<TItem>> columns = (ResolveColumnOrder(values)
            ?? _options.DisplayColumns)
            .Where(c => hidden is null || !hidden.Contains(c.Field))
            .ToArray();

        var engine = new GridQueryEngine<TItem>(_dataSource);
        var pageSize = Math.Max(_options.MaxPageSize, 1);
        var maxRows = format == GridExportFormats.Xlsx
            ? Math.Min(_options.MaxExportRows, Export.GridXlsxWriter.MaxDataRows)
            : _options.MaxExportRows;

        GridQuery PageQuery(int page) => normalization.Query with { Paging = new PageRequest(page, pageSize) };

        var first = await engine.ExecuteAsync(PageQuery(1), cancellationToken);

        // Every page of the export, capped at maxRows. The total may increase between pages
        // (live source): the cap holds anyway.
        async IAsyncEnumerable<IReadOnlyList<TItem>> Pages([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token = default)
        {
            var result = first;
            var written = 0;
            var page = 1;

            while (true)
            {
                var items = result.Page.Items.Take(maxRows - written).ToArray();
                written += items.Length;
                if (items.Length > 0)
                {
                    yield return items;
                }

                if (items.Length == 0 || written >= maxRows || written >= result.Page.TotalCount)
                {
                    yield break;
                }

                token.ThrowIfCancellationRequested();
                result = await engine.ExecuteAsync(PageQuery(++page), token);
            }
        }

        if (format == GridExportFormats.Xlsx)
        {
            return new GridExport(
                $"{_options.Id}.xlsx",
                Export.GridXlsxWriter.ContentType,
                first.Page.TotalCount,
                maxRows,
                (output, token) => Export.GridXlsxWriter.WriteAsync(output, _options.Title, columns, Pages(token), token));
        }

        async Task WriteCsvAsync(Stream output, CancellationToken token)
        {
            await using var writer = new StreamWriter(output, Export.GridCsvExporter.Encoding, bufferSize: 16 * 1024, leaveOpen: true);
            var buffer = new StringWriter(CultureInfo.InvariantCulture);

            Export.GridCsvExporter.WriteHeader(buffer, columns);
            await foreach (var items in Pages(token))
            {
                Export.GridCsvExporter.WriteRows(buffer, columns, items);
                await writer.WriteAsync(buffer.GetStringBuilder(), token);
                buffer.GetStringBuilder().Clear();
            }

            await writer.WriteAsync(buffer.GetStringBuilder(), token);   // header only, when nothing matched
            await writer.FlushAsync(token);
        }

        return new GridExport($"{_options.Id}.csv", "text/csv; charset=utf-8", first.Page.TotalCount, maxRows, WriteCsvAsync);
    }

    [Obsolete("Buffers the whole file in memory. Use CreateExportAsync and stream it instead.")]
    public async ValueTask<(string FileName, string Csv)> RenderExportAsync(GridRequestValues values, CancellationToken cancellationToken = default)
    {
        var export = await CreateExportAsync(values, GridExportFormats.Csv, cancellationToken);
        if (export.ExceedsLimit)
        {
            throw new GridException(export.LimitMessage);
        }

        using var stream = new MemoryStream();
        await export.WriteToAsync(stream, cancellationToken);
        stream.Position = 0;

        using var reader = new StreamReader(stream, Export.GridCsvExporter.Encoding, detectEncodingFromByteOrderMarks: true);
        return (export.FileName, await reader.ReadToEndAsync(cancellationToken));
    }

    public async ValueTask<string> RenderShellAsync(GridRequestValues values, CancellationToken cancellationToken = default)
    {
        var normalization = GridRequestParser.Parse(values, _options);
        var result = await new GridQueryEngine<TItem>(_dataSource).ExecuteAsync(normalization.Query, cancellationToken);
        // ?embed=1 lo pone la página anfitriona en el src del iframe; abierto a pelo, el grid
        // sigue trayendo su propio cromo.
        var embedded = values.Get("embed") is "1" or "true";
        var totals = await LoadTotalsAsync(result, cancellationToken);
        return await _renderer.RenderShellAsync(result, ResolveColumnOrder(values), embedded, cancellationToken, totals, ResolveHiddenFields(values));
    }

    public async ValueTask<string> RenderFragmentAsync(GridRequestValues values, CancellationToken cancellationToken = default)
    {
        var normalization = GridRequestParser.Parse(values, _options);
        var result = await new GridQueryEngine<TItem>(_dataSource).ExecuteAsync(normalization.Query, cancellationToken);
        var totals = await LoadTotalsAsync(result, cancellationToken);
        return await _renderer.RenderFragmentAsync(result, ResolveColumnOrder(values), cancellationToken, totals, ResolveHiddenFields(values));
    }
}
