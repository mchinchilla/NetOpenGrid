using NetOpenGrid.Domain.Abstractions;
using NetOpenGrid.Domain.GridQuerying;
using NetOpenGrid.Domain.Results;

namespace NetOpenGrid.Application.Engine;

public sealed record GridExecutionResult<T>(GridQuery Query, PageResult<T> Page);

/// <summary>Thin orchestrator: executes the normalized query against the data source (async boundary).</summary>
public sealed class GridQueryEngine<T>(IGridDataSource<T> dataSource)
{
    public async ValueTask<GridExecutionResult<T>> ExecuteAsync(GridQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        var page = await dataSource.LoadAsync(query, cancellationToken);
        return new GridExecutionResult<T>(query, page);
    }
}
