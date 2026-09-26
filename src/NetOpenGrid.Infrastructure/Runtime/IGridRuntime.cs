using NetOpenGrid.Application.Binding;
using NetOpenGrid.Application.Options;

namespace NetOpenGrid.Infrastructure.Runtime;

public sealed record GridRowsResponse(string Html, int TotalCount, int Page, int PageSize, int PageCount);

/// <summary>
/// An export (CSV or xlsx) whose first page is already loaded. Check <see cref="ExceedsLimit"/>
/// before sending headers; <see cref="WriteToAsync"/> then streams every row to the output.
/// </summary>
public sealed class GridExport(
    string fileName,
    string contentType,
    int totalCount,
    int maxRows,
    Func<Stream, CancellationToken, Task> writeToAsync)
{
    public string FileName { get; } = fileName;
    public string ContentType { get; } = contentType;
    public int TotalCount { get; } = totalCount;
    public int MaxRows { get; } = maxRows;
    public bool ExceedsLimit => TotalCount > MaxRows;

    public string LimitMessage =>
        $"The export has {TotalCount} rows, above the limit of {MaxRows}. Narrow it down with filters.";

    public Task WriteToAsync(Stream output, CancellationToken cancellationToken = default) =>
        writeToAsync(output, cancellationToken);
}

/// <summary>Non-generic facade resolved from keyed DI by grid id.</summary>
public interface IGridRuntime
{
    string Id { get; }

    ValueTask<GridRowsResponse> RenderRowsAsync(GridRequestValues values, CancellationToken cancellationToken = default);

    ValueTask<string> RenderValuesAsync(string field, GridRequestValues values, CancellationToken cancellationToken = default);

    ValueTask<GridExport> CreateExportAsync(GridRequestValues values, GridExportFormats format = GridExportFormats.Csv, CancellationToken cancellationToken = default);

    /// <summary>Formats this grid offers (<c>WithExportFormats</c>).</summary>
    GridExportFormats ExportFormats { get; }

    [Obsolete("Buffers the whole file in memory. Use CreateExportAsync and stream it instead.")]
    ValueTask<(string FileName, string Csv)> RenderExportAsync(GridRequestValues values, CancellationToken cancellationToken = default);

    ValueTask<string> RenderShellAsync(GridRequestValues values, CancellationToken cancellationToken = default);

    ValueTask<string> RenderFragmentAsync(GridRequestValues values, CancellationToken cancellationToken = default);
}
