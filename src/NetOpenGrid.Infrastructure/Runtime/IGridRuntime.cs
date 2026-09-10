using NetOpenGrid.Application.Binding;

namespace NetOpenGrid.Infrastructure.Runtime;

public sealed record GridRowsResponse(string Html, int TotalCount, int Page, int PageSize, int PageCount);

/// <summary>Non-generic facade resolved from keyed DI by grid id.</summary>
public interface IGridRuntime
{
    string Id { get; }

    ValueTask<GridRowsResponse> RenderRowsAsync(GridRequestValues values, CancellationToken cancellationToken = default);

    ValueTask<string> RenderValuesAsync(string field, GridRequestValues values, CancellationToken cancellationToken = default);

    ValueTask<(string FileName, string Csv)> RenderExportAsync(GridRequestValues values, CancellationToken cancellationToken = default);

    ValueTask<string> RenderShellAsync(GridRequestValues values, CancellationToken cancellationToken = default);

    ValueTask<string> RenderFragmentAsync(GridRequestValues values, CancellationToken cancellationToken = default);
}
