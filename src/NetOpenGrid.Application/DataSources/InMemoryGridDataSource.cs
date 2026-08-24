using NetOpenGrid.Application.Engine;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain.Abstractions;
using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Domain.GridQuerying;
using NetOpenGrid.Domain.Results;

namespace NetOpenGrid.Application.DataSources;

/// <summary>
/// In-memory data source: pulls a snapshot asynchronously and runs the
/// filter/sort/paginate pipeline over precompiled strategies.
/// </summary>
public class InMemoryGridDataSource<T> : IGridDataSource<T>, IGridValueCountSource<T>, IGridGroupingSource<T>
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

    /// <summary>Grouped load: filter → sort → group tree (pages contain groups, not rows).</summary>
    public async ValueTask<GroupedPageResult<T>> LoadGroupedAsync(GridQuery query, CancellationToken cancellationToken = default)
    {
        var snapshot = await _snapshotLoader(cancellationToken);
        var rows = InMemoryGridPipeline.FilterAndSort(snapshot, query, _options);
        return InMemoryGridPipeline.ProcessGrouped(rows, query, _options);
    }

    /// <summary>Excel-style counts: distinct raw values under the context query, excluding the column's own filter.</summary>
    public async ValueTask<GridValueCounts> GetValuesAsync(GridColumn<T> column, GridQuery context, CancellationToken cancellationToken = default)
    {
        if (column.RawKeyFormat is null || !column.IsFilterable)
        {
            return new GridValueCounts([], 0);
        }

        var snapshot = await _snapshotLoader(cancellationToken);
        var predicates = InMemoryGridPipeline.BuildPredicates(context, _options, excludeField: column.Field);
        var keyFormat = column.RawKeyFormat;

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in snapshot)
        {
            var keep = true;
            foreach (var predicate in predicates)
            {
                if (!predicate(item))
                {
                    keep = false;
                    break;
                }
            }

            if (!keep)
            {
                continue;
            }

            var key = keyFormat(item);
            if (key is null)
            {
                continue;
            }

            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        var ordered = counts
            .OrderBy(static kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var values = ordered
            .Take(_options.FilterValuesLimit)
            .Select(static kv => new GridValueCount(kv.Key, kv.Value))
            .ToList();

        return new GridValueCounts(values, ordered.Count);
    }
}
