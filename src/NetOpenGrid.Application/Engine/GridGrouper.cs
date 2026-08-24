using System.Globalization;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain.Abstractions;
using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Domain.GridQuerying;
using NetOpenGrid.Domain.Results;

namespace NetOpenGrid.Application.Engine;

/// <summary>
/// Builds the grouping tree over an already filtered + sorted row list.
/// Paths are "field=escapedValue" segments joined by "|"; values are
/// Uri.EscapeDataString-encoded so they survive URL round-trips.
/// </summary>
public static class GridGrouper<T>
{
    public static GroupedPageResult<T> Group(
        IReadOnlyList<T> rows,
        GridQuery query,
        GridOptions<T> options)
    {
        var paging = query.Paging.Normalized(options.MaxPageSize);
        var groupColumns = ResolveGroupColumns(query.GroupFields, options);

        if (groupColumns.Count == 0)
        {
            return new GroupedPageResult<T>([], 0, paging.Page, paging.PageSize);
        }

        var ordered = StableSort.Sort(rows, BindGroupSorts(groupColumns, query.Sorts));
        var expanded = query.Expanded.ToHashSet(StringComparer.Ordinal);

        var topLevel = BuildLevel(ordered, 0, string.Empty, groupColumns, expanded);
        var totalGroups = topLevel.Count;

        var pageGroups = topLevel
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .ToList();

        return new GroupedPageResult<T>(pageGroups, totalGroups, paging.Page, paging.PageSize);
    }

    private static List<GridColumn<T>> ResolveGroupColumns(IEnumerable<string> fields, GridOptions<T> options)
    {
        var columns = new List<GridColumn<T>>();
        foreach (var field in fields)
        {
            if (options.TryGetColumn(field, out var column) && column.RawKeyFormat is not null)
            {
                columns.Add(column);
            }
        }

        return columns;
    }

    private static List<(ISortStrategy<T> Strategy, bool Descending)> BindGroupSorts(
        IReadOnlyList<GridColumn<T>> groupColumns,
        IReadOnlyList<SortDescriptor> sorts)
    {
        var bound = new List<(ISortStrategy<T>, bool)>(groupColumns.Count);

        foreach (var column in groupColumns)
        {
            var requested = sorts.FirstOrDefault(s => s.Field == column.Field);
            var descending = requested?.Direction == SortDirection.Descending;

            var strategy = column.SortStrategy ?? new RawKeySortStrategy(column);
            bound.Add((strategy, descending));
        }

        return bound;
    }

    private static List<GridGroup<T>> BuildLevel(
        IReadOnlyList<T> rows,
        int level,
        string parentPath,
        IReadOnlyList<GridColumn<T>> columns,
        HashSet<string> expanded)
    {
        var keyFormat = columns[level].RawKeyFormat!;
        var field = columns[level].Field;

        List<GridGroup<T>>? groups = null;
        List<T>? bucket = null;
        string? currentKey = null;

        void Flush()
        {
            if (bucket is null || currentKey is null)
            {
                return;
            }

            var escaped = Uri.EscapeDataString(currentKey);
            var path = parentPath.Length == 0
                ? $"{field}={escaped}"
                : $"{parentPath}|{field}={escaped}";

            var isExpanded = expanded.Contains(path);
            var children = new List<GridGroup<T>>();
            var leafRows = new List<T>();

            if (isExpanded)
            {
                if (level + 1 < columns.Count)
                {
                    children = BuildLevel(bucket, level + 1, path, columns, expanded);
                }
                else
                {
                    leafRows = bucket;
                }
            }

            (groups ??= []).Add(new GridGroup<T>(
                path,
                currentKey,
                level,
                bucket.Count,
                children,
                leafRows));
        }

        foreach (var row in rows)
        {
            var key = keyFormat(row) ?? string.Empty;

            if (currentKey is null || !string.Equals(key, currentKey, StringComparison.Ordinal))
            {
                Flush();
                currentKey = key;
                bucket = new List<T>();
            }

            bucket!.Add(row);
        }

        Flush();
        return groups ?? [];
    }

    private sealed class RawKeySortStrategy(GridColumn<T> column) : ISortStrategy<T>
    {
        public int Compare(T x, T y)
        {
            var kx = column.RawKeyFormat!(x);
            var ky = column.RawKeyFormat!(y);
            return string.Compare(kx, ky, StringComparison.OrdinalIgnoreCase);
        }
    }
}
