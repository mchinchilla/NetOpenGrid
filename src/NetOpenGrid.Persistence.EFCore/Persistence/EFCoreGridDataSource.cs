using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain;
using NetOpenGrid.Domain.Abstractions;
using NetOpenGrid.Domain.GridQuerying;
using NetOpenGrid.Domain.Results;

namespace NetOpenGrid.Persistence;

/// <summary>
/// EF Core data source: composes Where/OrderBy/Skip/Take on the consumer's
/// IQueryable&lt;T&gt; (full SQL push-down) using the same column definitions,
/// operator whitelist and invariant parsing rules as the in-memory pipeline.
/// </summary>
public sealed class EFCoreGridDataSource<T> : IGridDataSource<T> where T : class
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

    private static async ValueTask<PageResult<T>> ExecuteAsync(
        IQueryable<T> source,
        GridQuery query,
        GridOptions<T> options,
        CancellationToken cancellationToken)
    {
        var paging = NormalizePaging(query, options);

        var filtered = source;
        foreach (var filter in query.Filters)
        {
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

        var totalCount = await filtered.CountAsync(cancellationToken);

        var sorted = filtered;
        var thenBy = false;
        foreach (var sort in query.Sorts)
        {
            if (options.TryGetColumn(sort.Field, out var column) && column.SelectorExpression is not null)
            {
                sorted = EFGridExpressionTranslator.ApplySort(sorted, column, sort.Direction, thenBy);
                thenBy = true;
            }
        }

        var items = await sorted
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .ToListAsync(cancellationToken);

        return new PageResult<T>(items, totalCount, paging.Page, paging.PageSize);
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
}
