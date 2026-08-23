using NetOpenGrid.Application.Engine;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain.Abstractions;
using NetOpenGrid.Domain.GridQuerying;
using NetOpenGrid.Domain.Results;

namespace NetOpenGrid.Application.DataSources;

/// <summary>
/// In-memory data source: pulls a snapshot asynchronously and runs the
/// filter/sort/paginate pipeline over precompiled strategies.
/// </summary>
public class InMemoryGridDataSource<T> : IGridDataSource<T>
{
    private readonly GridOptions<T> _options;
    private readonly Func<CancellationToken, ValueTask<IReadOnlyList<T>>> _snapshotLoader;

    public InMemoryGridDataSource(GridOptions<T> options, Func<CancellationToken, ValueTask<IReadOnlyList<T>>> snapshotLoader)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(snapshotLoader);
        _options = options;
        _snapshotLoader = snapshotLoader;
    }

    public InMemoryGridDataSource(GridOptions<T> options, IReadOnlyList<T> snapshot)
        : this(options, _ => ValueTask.FromResult(snapshot))
    {
    }

    public async ValueTask<PageResult<T>> LoadAsync(GridQuery query, CancellationToken cancellationToken = default)
    {
        var snapshot = await _snapshotLoader(cancellationToken);
        return InMemoryGridPipeline.Process(snapshot, query, _options);
    }
}
