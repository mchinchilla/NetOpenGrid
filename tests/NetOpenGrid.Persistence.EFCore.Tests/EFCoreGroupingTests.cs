using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NetOpenGrid.Application.Builders;
using NetOpenGrid.Application.DataSources;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain;
using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Domain.GridQuerying;
using NetOpenGrid.Domain.Results;
using Xunit;

namespace NetOpenGrid.Persistence.EFCore.Tests;

/// <summary>
/// Grouping pushed down to SQL: the tree matches the in-memory grouper (keys, paths, counts, labels,
/// order, rows of expanded groups, subtotals) while the database does the grouping. Collapsed groups
/// only run <c>GROUP BY</c> queries, so no row of the filtered set is ever loaded for them.
/// </summary>
public sealed class EFCoreGroupingTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TestDb _db;
    private readonly CommandLog _log = new();

    public EFCoreGroupingTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new TestDb(new DbContextOptionsBuilder<TestDb>().UseSqlite(_connection).AddInterceptors(_log).Options);
        _db.Database.EnsureCreated();
        _db.Products.AddRange(EfProductData.All);
        _db.SaveChanges();
        _log.Commands.Clear();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Collapsed_groups_match_the_in_memory_tree_and_only_run_group_by_queries()
    {
        var query = Query(["category"]);

        var sql = await Ef().LoadGroupedAsync(query);
        var memory = await Memory().LoadGroupedAsync(query);

        AssertSameTree(memory, sql);
        Assert.NotEmpty(_log.Commands);
        Assert.All(_log.Commands, c => Assert.Contains("GROUP BY", c, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Groups_are_paged_in_the_database_with_the_total_of_every_group()
    {
        var query = Query(["category"], page: 2, pageSize: 2);

        var sql = await Ef().LoadGroupedAsync(query);
        var memory = await Memory().LoadGroupedAsync(query);

        Assert.Equal(4, sql.TotalGroups);
        Assert.Equal(2, sql.Groups.Count);
        AssertSameTree(memory, sql);
        Assert.Contains(_log.Commands, c => c.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(_log.Commands, c => c.Contains("DISTINCT", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_last_page_that_is_not_full_needs_no_count_query()
    {
        await Ef().LoadGroupedAsync(Query(["category"], pageSize: 10));

        Assert.DoesNotContain(_log.Commands, c => c.Contains("DISTINCT", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Descending_sort_on_the_group_field_orders_the_groups()
    {
        var query = Query(["category"], sorts: [new SortDescriptor("category", SortDirection.Descending)]);

        var sql = await Ef().LoadGroupedAsync(query);

        Assert.Equal(["Toys", "Sports", "Home", "Electronics"], sql.Groups.Select(g => g.Value));
    }

    [Fact]
    public async Task Expanded_nested_groups_and_their_rows_match_the_in_memory_tree()
    {
        var query = Query(
            ["category", "active"],
            ["category=Electronics", "category=Electronics|active=true"],
            sorts: [new SortDescriptor("price", SortDirection.Descending)]);

        var sql = await Ef().LoadGroupedAsync(query);
        var memory = await Memory().LoadGroupedAsync(query);

        AssertSameTree(memory, sql);
        var active = sql.Groups.Single(g => g.Value == "Electronics").Children.Single(c => c.Value == "true");
        Assert.NotEmpty(active.Rows);
        Assert.Equal(active.Rows.OrderByDescending(p => p.Price).Select(p => p.Id), active.Rows.Select(p => p.Id));
    }

    [Fact]
    public async Task Rows_of_an_expanded_group_are_capped_and_the_count_keeps_the_total()
    {
        var options = Options(b => b.WithMaxGroupRows(3));
        var query = Query(["category"], ["category=Electronics"]);

        var electronics = (await Ef(options).LoadGroupedAsync(query)).Groups.Single(g => g.Value == "Electronics");

        Assert.Equal(EfProductData.All.Count(p => p.Category == "Electronics"), electronics.Count);
        Assert.Equal(3, electronics.Rows.Count);
        Assert.Contains(_log.Commands, c => c.Contains("LIMIT", StringComparison.OrdinalIgnoreCase) && !c.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Labels_use_the_column_format_and_subtotals_cover_every_row_of_the_group()
    {
        var options = new GridOptionsBuilder<EfProduct>()
            .WithId("ef-formatted")
            .WithAggregateRows(GridAggregateRows.GroupHeader)
            .AddColumn(p => p.Id)
            .AddColumn(p => p.Category, c => c.Format(v => "Category " + v))
            .AddColumn(p => p.Price, c => c.Aggregate(GridAggregate.Sum | GridAggregate.Max))
            .Build();
        var query = Query(["category"]);

        var sql = await Ef(options).LoadGroupedAsync(query);
        var memory = await Memory(options).LoadGroupedAsync(query);

        AssertSameTree(memory, sql);
        var home = sql.Groups.Single(g => g.Value == "Home");
        Assert.Equal("Category Home", home.Label);
        Assert.Equal("category=Home", home.Path);
        Assert.Equal(EfProductData.All.Where(p => p.Category == "Home").Sum(p => p.Price), home.Aggregates.For("price")!.Sum);
    }

    [Fact]
    public async Task Typed_keys_group_in_their_natural_order_with_the_same_raw_keys()
    {
        foreach (var field in new[] { "active", "stock", "releasedOn" })
        {
            var query = Query([field], pageSize: 100);
            var sql = await Ef().LoadGroupedAsync(query);
            var memory = await Memory().LoadGroupedAsync(query);
            AssertSameTree(memory, sql);
        }
    }

    private EFCoreGridDataSource<EfProduct> Ef(GridOptions<EfProduct>? options = null) =>
        new(options ?? Options(), _ => ValueTask.FromResult<IQueryable<EfProduct>>(_db.Products.AsNoTracking()));

    private static InMemoryGridDataSource<EfProduct> Memory(GridOptions<EfProduct>? options = null) =>
        new(options ?? Options(), EfProductData.All);

    private static GridOptions<EfProduct> Options(Action<GridOptionsBuilder<EfProduct>>? extra = null) => EfTestGrid.Options(extra);

    private static GridQuery Query(
        string[] groupBy,
        string[]? expanded = null,
        int page = 1,
        int pageSize = 10,
        SortDescriptor[]? sorts = null) =>
        new(new PageRequest(page, pageSize), sorts ?? [], [], null, groupBy, expanded);

    private static void AssertSameTree(GroupedPageResult<EfProduct> expected, GroupedPageResult<EfProduct> actual)
    {
        Assert.Equal(expected.TotalGroups, actual.TotalGroups);
        AssertSameGroups(expected.Groups, actual.Groups);
    }

    private static void AssertSameGroups(IReadOnlyList<GridGroup<EfProduct>> expected, IReadOnlyList<GridGroup<EfProduct>> actual)
    {
        Assert.Equal(expected.Select(g => (g.Path, g.Value, g.Label, g.Level, g.Count)), actual.Select(g => (g.Path, g.Value, g.Label, g.Level, g.Count)));
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Rows.Select(r => r.Id), actual[i].Rows.Select(r => r.Id));
            Assert.Equal(expected[i].Aggregates.IsEmpty, actual[i].Aggregates.IsEmpty);
            AssertSameGroups(expected[i].Children, actual[i].Children);
        }
    }

    /// <summary>The SQL each test sends, to prove what the database did and what it did not.</summary>
    private sealed class CommandLog : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
