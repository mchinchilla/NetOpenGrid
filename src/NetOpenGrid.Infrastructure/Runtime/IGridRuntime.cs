using NetOpenGrid.Application.Binding;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain.GridQuerying;

namespace NetOpenGrid.Infrastructure.Runtime;

public sealed record GridRowsResponse(string Html, int TotalCount, int Page, int PageSize, int PageCount, bool TotalCountIsCapped = false)
{
    /// <summary>
    /// Readable labels for the values of the active filters (field → raw value → label), from the
    /// columns' <c>FilterValueLabels</c>. Empty when no filtered column has a resolver. The rows
    /// endpoint sends it in the <c>X-Grid-Filter-Labels</c> header (see <see cref="GridFilterLabels"/>).
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> FilterLabels { get; init; } = GridFilterLabels.None;

    /// <summary>
    /// Fields with a filter whose value does not parse to the column's type. Such a filter matches
    /// nothing, so the response has no rows. The rows endpoint sends the fields in the
    /// <c>X-Grid-Invalid-Filters</c> header (a JSON array) and the client marks their chips as invalid.
    /// </summary>
    public IReadOnlyList<string> InvalidFilters { get; init; } = [];
}

/// <summary>Wire format of the filter value labels (field → raw value → label).</summary>
public static class GridFilterLabels
{
    /// <summary>Response header of the rows endpoint that carries the labels.</summary>
    public const string HeaderName = "X-Grid-Filter-Labels";

    /// <summary>Response header of the rows endpoint that lists the fields with an invalid filter.</summary>
    public const string InvalidFiltersHeaderName = "X-Grid-Invalid-Filters";

    /// <summary>
    /// Above this many characters the header is left out and the chips show raw values: reverse
    /// proxies cap the size of the response headers (nginx: 4-8 KB for all of them together).
    /// </summary>
    public const int MaxHeaderLength = 3072;

    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> None { get; } =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(0);

    /// <summary>
    /// The labels as JSON in plain ASCII: the default encoder writes every non-ASCII character
    /// (and the HTML-sensitive ones) as a \uXXXX escape. That makes the same text valid as an HTTP
    /// header value, which cannot carry an accented letter as is, and safe inside a script block.
    /// </summary>
    public static string ToJson(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> labels) =>
        System.Text.Json.JsonSerializer.Serialize(labels);
}

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

    /// <summary>
    /// Readable labels for the values of the query's filters (field → raw value → label), resolved
    /// by the columns' <c>FilterValueLabels</c>: the same text the filter chips show, for a host
    /// that describes the filters somewhere else (the heading of its own Excel export, a report).
    /// Each resolver runs once, one after another; one that fails is logged and left out, so its
    /// values keep their raw text. Fields without a resolver or without a match are not listed.
    /// </summary>
    /// <param name="query">The query whose filters are described.</param>
    /// <param name="services">The current request's services, handed to the resolvers.</param>
    /// <param name="cancellationToken">Cancels the resolvers.</param>
    ValueTask<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> ResolveFilterLabelsAsync(
        GridQuery query,
        IServiceProvider services,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(GridFilterLabels.None);

    /// <summary>
    /// Same as the overload above, from raw request values (parsed and whitelisted like any grid
    /// request); the resolvers get <see cref="GridRequestValues.Services"/>.
    /// </summary>
    ValueTask<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> ResolveFilterLabelsAsync(
        GridRequestValues values,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(GridFilterLabels.None);
}
