using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetOpenGrid.Application.Engine;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain;
using NetOpenGrid.Domain.Abstractions;
using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Domain.GridQuerying;
using NetOpenGrid.Domain.Results;

namespace NetOpenGrid.Persistence;

/// <summary>
/// EF Core data source: composes Where/OrderBy/Skip/Take on the consumer's
/// IQueryable&lt;T&gt; (full SQL push-down) using the same column definitions,
/// operator whitelist and invariant parsing rules as the in-memory pipeline.
/// </summary>
public sealed class EFCoreGridDataSource<T> : IGridDataSource<T>, IGridValueCountSource<T>, IGridGroupingSource<T> where T : class
{
    private readonly GridOptions<T> _options;
    private readonly IServiceProvider? _serviceProvider;
    private readonly Func<IServiceProvider, IQueryable<T>>? _scopedQueryFactory;
    private readonly Func<CancellationToken, ValueTask<IQueryable<T>>>? _queryFactory;

    /// <summary>Scoped-friendly constructor: resolves DbContext per request from a new scope.</summary>
    public EFCoreGridDataSource(GridOptions<T> options, IServiceProvider serviceProvider, Func<IServiceProvider, IQueryable<T>> scopedQueryFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(scopedQueryFactory);
        _options = options;
        _serviceProvider = serviceProvider;
        _scopedQueryFactory = scopedQueryFactory;
        ValidateColumns(options);
    }

    /// <summary>Direct constructor (tests, pre-built query providers).</summary>
    public EFCoreGridDataSource(GridOptions<T> options, Func<CancellationToken, ValueTask<IQueryable<T>>> queryFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(queryFactory);
        _options = options;
        _queryFactory = queryFactory;
        ValidateColumns(options);
    }

    public async ValueTask<PageResult<T>> LoadAsync(GridQuery query, CancellationToken cancellationToken = default)
    {
        if (_scopedQueryFactory is not null)
        {
            using var scope = _serviceProvider!.CreateScope();
            return await ExecuteAsync(_scopedQueryFactory(scope.ServiceProvider), query, _options, cancellationToken);
        }

        var source = await _queryFactory!(cancellationToken);
        return await ExecuteAsync(source, query, _options, cancellationToken);
    }

    /// <summary>Excel-style distinct counts: GROUP BY on the column selector, excluding the column's own filter.</summary>
    public async ValueTask<GridValueCounts> GetValuesAsync(GridColumn<T> column, GridQuery context, CancellationToken cancellationToken = default)
    {
        if (column.SelectorExpression is null || !column.IsFilterable)
        {
            return new GridValueCounts([], 0);
        }

        IQueryable<T> source;
        if (_scopedQueryFactory is not null)
        {
            using var scope = _serviceProvider!.CreateScope();
            source = _scopedQueryFactory(scope.ServiceProvider);
            return await GetValuesCoreAsync(source, column, context, _options, cancellationToken);
        }

        source = await _queryFactory!(cancellationToken);
        return await GetValuesCoreAsync(source, column, context, _options, cancellationToken);
    }

    /// <summary>
    /// Grouped load: filter + sort are pushed down to SQL; the grouping tree is
    /// built in memory over the matching rows (documented v1 trade-off).
    /// </summary>
    public async ValueTask<GroupedPageResult<T>> LoadGroupedAsync(GridQuery query, CancellationToken cancellationToken = default)
    {
        if (_scopedQueryFactory is not null)
        {
            using var scope = _serviceProvider!.CreateScope();
            var scopedSource = ApplyFilters(_scopedQueryFactory(scope.ServiceProvider), query, _options);
            var scopedRows = await ApplySorts(scopedSource, query, _options).ToListAsync(cancellationToken);
            return GridGrouper<T>.Group(scopedRows, query, _options);
        }

        var source = ApplyFilters(await _queryFactory!(cancellationToken), query, _options);
        var rows = await ApplySorts(source, query, _options).ToListAsync(cancellationToken);
        return GridGrouper<T>.Group(rows, query, _options);
    }

    private static async ValueTask<PageResult<T>> ExecuteAsync(
        IQueryable<T> source,
        GridQuery query,
        GridOptions<T> options,
        CancellationToken cancellationToken)
    {
        var paging = NormalizePaging(query, options);
        var filtered = ApplyFilters(source, query, options);
        var totalCount = await filtered.CountAsync(cancellationToken);
        var sorted = ApplySorts(filtered, query, options);

        var items = await sorted
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .ToListAsync(cancellationToken);

        return new PageResult<T>(items, totalCount, paging.Page, paging.PageSize);
    }

    private static async ValueTask<GridValueCounts> GetValuesCoreAsync(
        IQueryable<T> source,
        GridColumn<T> column,
        GridQuery context,
        GridOptions<T> options,
        CancellationToken cancellationToken)
    {
        var filtered = ApplyFilters(source, context, options, excludeField: column.Field);

        var lambda = column.SelectorExpression!;
        var keyLambda = Expression.Lambda<Func<T, object?>>(
            Expression.Convert(lambda.Body, typeof(object)),
            (ParameterExpression)lambda.Parameters[0]);

        var rows = await filtered
            .GroupBy(keyLambda, ElementSelector())
            .Select(KeyValueProjection.Build())
            .ToListAsync(cancellationToken);

        var values = rows
            .Select(row => new GridValueCount(column.KeyFormatter?.Invoke(row.Key) ?? row.Key?.ToString() ?? string.Empty, row.Count))
            .OrderBy(static v => v.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new GridValueCounts(
            values.Take(options.FilterValuesLimit).ToList(),
            values.Count);
    }

    private static IQueryable<T> ApplySorts(IQueryable<T> source, GridQuery query, GridOptions<T> options)
    {
        var sorted = source;
        var thenBy = false;
        foreach (var sort in query.Sorts)
        {
            if (options.TryGetColumn(sort.Field, out var column) && column.SelectorExpression is not null)
            {
                sorted = EFGridExpressionTranslator.ApplySort(sorted, column, sort.Direction, thenBy);
                thenBy = true;
            }
        }

        return sorted;
    }

    private static IQueryable<T> ApplyFilters(IQueryable<T> source, GridQuery query, GridOptions<T> options, string? excludeField = null)
    {
        var filtered = source;

        foreach (var filter in query.Filters)
        {
            if (excludeField is not null && string.Equals(filter.Field, excludeField, StringComparison.Ordinal))
            {
                continue;
            }

            if (!options.TryGetColumn(filter.Field, out var column))
            {
                continue;
            }

            if (EFGridExpressionTranslator.CreateFilter(column, filter.Operator, filter.Value) is { } predicate)
            {
                filtered = filtered.Where(predicate);
            }
        }

        if (!string.IsNullOrEmpty(query.Search) &&
            EFGridExpressionTranslator.CreateSearch(options.Columns, query.Search) is { } searchPredicate)
        {
            filtered = filtered.Where(searchPredicate);
        }

        return filtered;
    }

    private static PageRequest NormalizePaging(GridQuery query, GridOptions<T> options)
    {
        var requested = query.Paging;
        var pageSize = requested.PageSize > 0 ? requested.PageSize : Math.Min(options.DefaultPageSize, options.MaxPageSize);
        return new PageRequest(requested.Page < 1 ? 1 : requested.Page, pageSize).Normalized(options.MaxPageSize);
    }

    private static void ValidateColumns(GridOptions<T> options)
    {
        var missing = options.Columns
            .Where(static c => (c.IsSortable || c.IsFilterable || c.IsSearchable) && c.SelectorExpression is null)
            .Select(static c => c.Field)
            .ToArray();

        if (missing.Length > 0)
        {
            throw new GridConfigurationException(
                "EFCoreGridDataSource requires expression selectors for sortable/filterable/searchable columns. " +
                $"Use the AddColumn(e => e.Property, ...) expression overload for: {string.Join(", ", missing)}.");
        }
    }

    internal sealed record KeyValueCount(object? Key, int Count)
    {
        public static readonly ConstructorInfo Ctor =
            typeof(KeyValueCount).GetConstructor([typeof(object), typeof(int)])!;
    }

    private static Expression<Func<T, T>> ElementSelector()
    {
        var parameter = Expression.Parameter(typeof(T), "x");
        return Expression.Lambda<Func<T, T>>(parameter, parameter);
    }

    private static class KeyValueProjection
    {
        internal static Expression<Func<IGrouping<object?, T>, KeyValueCount>> Build()
        {
            var g = Expression.Parameter(typeof(IGrouping<object?, T>), "g");
            var body = Expression.New(
                EFCoreGridDataSource<T>.KeyValueCount.Ctor,
                Expression.Property(g, nameof(IGrouping<object?, T>.Key)),
                Expression.Call(typeof(Enumerable), nameof(Enumerable.Count), [typeof(T)], g));
            return Expression.Lambda<Func<IGrouping<object?, T>, KeyValueCount>>(body, g);
        }
    }
}
