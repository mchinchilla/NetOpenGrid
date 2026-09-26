using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NetOpenGrid.Application.Builders;
using NetOpenGrid.Application.DataSources;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Domain.GridQuerying;
using NetOpenGrid.Domain.Results;
using Xunit;

namespace NetOpenGrid.Persistence.EFCore.Tests;

public sealed class EFCoreAggregateTests : IDisposable
{
    private const GridAggregate AllFunctions = GridAggregate.Sum | GridAggregate.Avg | GridAggregate.Min | GridAggregate.Max;

    private readonly SqliteConnection _connection;
    private readonly TestDb _db;

    private readonly GridOptions<EfProduct> _options = new GridOptionsBuilder<EfProduct>()
        .WithId("ef-aggregates")
        .AddColumn(p => p.Name)
        .AddColumn(p => p.Category)
        .AddColumn(p => p.Price, c => c.Aggregate(AllFunctions))
        .AddColumn(p => p.Stock, c => c.Aggregate(AllFunctions))
        .Build();

    public EFCoreAggregateTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _db = new TestDb(new DbContextOptionsBuilder<TestDb>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _db.Products.AddRange(EfProductData.All);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private EFCoreGridDataSource<EfProduct> CreateSource() =>
        new(_options, _ => ValueTask.FromResult<IQueryable<EfProduct>>(_db.Products.AsNoTracking()));

    private static GridQuery Query(params FilterDescriptor[] filters) => new(new PageRequest(1, 5), [], filters);

    [Fact]
    public async Task Aggregates_Are_Computed_In_Sql_Over_The_Filtered_Set()
    {
        var query = Query(new FilterDescriptor("category", FilterOperator.Equals, "Home"));
        var home = EfProductData.All.Where(p => p.Category == "Home").ToArray();

        var totals = await CreateSource().GetAggregatesAsync(query);

        var price = totals.For("price")!;
        Assert.Equal(home.Sum(p => p.Price), price.Sum);
        Assert.Equal(home.Average(p => p.Price), price.Avg!.Value, 10);
        Assert.Equal(home.Min(p => p.Price), price.Min);
        Assert.Equal(home.Max(p => p.Price), price.Max);

        var stock = totals.For("stock")!;
        Assert.Equal(home.Sum(p => p.Stock), stock.Sum);
        Assert.Equal((decimal)home.Average(p => p.Stock), stock.Avg!.Value, 10);
        Assert.Equal(home.Min(p => p.Stock), stock.Min);
        Assert.Equal(home.Max(p => p.Stock), stock.Max);
    }

    [Fact]
    public async Task Aggregates_Match_The_In_Memory_Source()
    {
        var query = Query(new FilterDescriptor("price", FilterOperator.GreaterThan, "50"));

        var sql = await CreateSource().GetAggregatesAsync(query);
        var memory = await new InMemoryGridDataSource<EfProduct>(_options, EfProductData.All).GetAggregatesAsync(query);

        foreach (var field in new[] { "price", "stock" })
        {
            var expected = memory.For(field)!;
            var actual = sql.For(field)!;
            Assert.Equal(expected.Sum, actual.Sum);
            Assert.Equal(expected.Min, actual.Min);
            Assert.Equal(expected.Max, actual.Max);
            Assert.Equal(expected.Avg!.Value, actual.Avg!.Value, 10);
        }
    }

    [Fact]
    public async Task Aggregates_Run_As_A_Single_Sql_Query()
    {
        var commands = new List<string>();
        using var db = new TestDb(new DbContextOptionsBuilder<TestDb>()
            .UseSqlite(_connection)
            .LogTo(commands.Add, [RelationalEventId.CommandExecuted])
            .Options);
        var source = new EFCoreGridDataSource<EfProduct>(_options,
            _ => ValueTask.FromResult<IQueryable<EfProduct>>(db.Products.AsNoTracking()));

        await source.GetAggregatesAsync(Query());

        var sql = Assert.Single(commands);
        Assert.Contains("SUM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AVG", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MIN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MAX", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Empty_Result_Yields_Nulls()
    {
        var totals = await CreateSource().GetAggregatesAsync(Query(new FilterDescriptor("category", FilterOperator.Equals, "Nope")));

        Assert.Equal(new GridAggregateValues(null, null, null, null), totals.For("price"));
    }
}
