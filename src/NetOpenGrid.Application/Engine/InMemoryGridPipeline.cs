using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain.Abstractions;
using NetOpenGrid.Domain.GridQuerying;
using NetOpenGrid.Domain.Results;

namespace NetOpenGrid.Application.Engine;

/// <summary>
/// Stable multi-key sort using a decorated-index array sort:
/// one allocation, deterministic ordering, no LINQ iterator chains.
/// </summary>
internal static class StableSort
{
    private readonly record struct Entry(int Order, int ItemIndex);

    public static T[] Sort<T>(IReadOnlyList<T> source, IReadOnlyList<(ISortStrategy<T> Strategy, bool Descending)> sorts)
    {
        var count = source.Count;
        if (count <= 1)
        {
            return source is T[] arr ? arr : [.. source];
        }

        var entries = new Entry[count];
        for (var i = 0; i < count; i++)
        {
            entries[i] = new Entry(i, i);
        }

        Array.Sort(entries, (a, b) =>
        {
            for (var s = 0; s < sorts.Count; s++)
            {
                var (strategy, descending) = sorts[s];
                var result = strategy.Compare(source[a.ItemIndex], source[b.ItemIndex]);
                if (result != 0)
                {
                    return descending ? -result : result;
                }
            }

            return a.Order.CompareTo(b.Order);
        });

        var result = new T[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = source[entries[i].ItemIndex];
        }

        return result;
    }
}

/// <summary>In-memory pipeline: filter → global search → stable sort → count → slice.</summary>
internal static class InMemoryGridPipeline
{
    /// <summary>Groups an already filtered + sorted row list into the paging-by-groups tree.</summary>
    public static GroupedPageResult<T> ProcessGrouped<T>(
        IReadOnlyList<T> rows,
        GridQuery query,
        Options.GridOptions<T> options)
        => GridGrouper<T>.Group(rows, query, options);


    public static PageResult<T> Process<T>(
        IReadOnlyList<T> source,
        GridQuery query,
        GridOptions<T> options)
    {
        // Defensive: default(struct PageRequest) yields zeros; fall back to configured defaults.
        var requested = query.Paging;
        var pageSize = requested.PageSize > 0 ? requested.PageSize : Math.Min(options.DefaultPageSize, options.MaxPageSize);
        var paging = new PageRequest(requested.Page < 1 ? 1 : requested.Page, pageSize)
            .Normalized(options.MaxPageSize);

        var sortedItems = FilterAndSort(source, query, options);
        var totalCount = sortedItems.Length;

        var take = Math.Min(paging.PageSize, Math.Max(totalCount - paging.Skip, 0));
        var items = new List<T>(take > 0 ? take : 0);
        for (var i = paging.Skip; i < paging.Skip + take; i++)
        {
            items.Add(sortedItems[i]);
        }

        return new PageResult<T>(items, totalCount, paging.Page, paging.PageSize);
    }

    /// <summary>Filter → global search → stable sort. Shared by the flat and grouped pipelines.</summary>
    internal static T[] FilterAndSort<T>(
        IReadOnlyList<T> source,
        GridQuery query,
        GridOptions<T> options)
    {
        var predicates = BuildPredicates(query, options);
        var working = ApplyPredicates(source, predicates);

        var sorts = BindSorts(query.Sorts, options);
        return sorts.Count > 0 ? StableSort.Sort(working, sorts) : working is T[] a ? a : [.. working];
    }

    internal static List<Func<T, bool>> BuildPredicates<T>(GridQuery query, GridOptions<T> options, string? excludeField = null)
    {
        var predicates = new List<Func<T, bool>>();

        foreach (var filter in query.Filters)
        {
            if (excludeField is not null && string.Equals(filter.Field, excludeField, StringComparison.Ordinal))
            {
                continue;
            }

            if (!options.TryGetColumn(filter.Field, out var column) ||
                column.FilterFactory is not { } factory)
            {
                continue;
            }

            if (factory.Create(filter.Operator, filter.Value) is { } strategy)
            {
                predicates.Add(strategy.Matches);
            }
        }

        if (!string.IsNullOrEmpty(query.Search))
        {
            var term = query.Search;
            var searchers = options.Columns
                .Where(static c => c.SearchStrategy is not null)
                .Select(static c => c.SearchStrategy!)
                .ToArray();

            if (searchers.Length > 0)
            {
                predicates.Add(item =>
                {
                    foreach (var searcher in searchers)
                    {
                        if (searcher.Matches(item, term))
                        {
                            return true;
                        }
                    }

                    return false;
                });
            }
        }

        return predicates;
    }

    private static List<T> ApplyPredicates<T>(IReadOnlyList<T> source, List<Func<T, bool>> predicates)
    {
        if (predicates.Count == 0)
        {
            return source as List<T> ?? [.. source];
        }

        var result = new List<T>(source.Count);
        foreach (var item in source)
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

            if (keep)
            {
                result.Add(item);
            }
        }

        return result;
    }

    private static List<(ISortStrategy<T>, bool)> BindSorts<T>(
        IReadOnlyList<SortDescriptor> sorts,
        GridOptions<T> options)
    {
        var bound = new List<(ISortStrategy<T>, bool)>(sorts.Count);
        foreach (var sort in sorts)
        {
            if (options.TryGetColumn(sort.Field, out var column) &&
                column.SortStrategy is { } strategy)
            {
                bound.Add((strategy, sort.Direction == SortDirection.Descending));
            }
        }

        return bound;
    }
}
