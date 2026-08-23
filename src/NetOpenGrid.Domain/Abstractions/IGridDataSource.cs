using NetOpenGrid.Domain.GridQuerying;
using NetOpenGrid.Domain.Results;

namespace NetOpenGrid.Domain.Abstractions;

/// <summary>
/// Async data source contract. Implementations receive the normalized query and return
/// the filtered, sorted page plus total count (push-down for DB sources, in-memory for local ones).
/// </summary>
public interface IGridDataSource<T>
{
    ValueTask<PageResult<T>> LoadAsync(GridQuery query, CancellationToken cancellationToken = default);
}
