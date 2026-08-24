using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NetOpenGrid.Application.Builders;
using NetOpenGrid.Application.DataSources;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain.Abstractions;
using NetOpenGrid.Domain;
using NetOpenGrid.Domain.GridQuerying;
using Xunit;

namespace NetOpenGrid.Persistence.EFCore.Tests;

public sealed class EFCoreGridDataSourceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TestDb _db;
    private readonly GridOptions<EfProduct> _options = EfTestGrid.Options();

    public EFCoreGridDataSourceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var dbOptions = new DbContextOptionsBuilder<TestDb>()
            .UseSqlite(_connection)
            .Options;

        _db = new TestDb(dbOptions);
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

    [Fact]
    public async Task Sort_PushesDown_AndReturnsPage()
    {
        var page = await CreateSource().LoadAsync(new GridQuery(
            new PageRequest(1, 5),
            [new SortDescriptor("price", SortDirection.Descending)],
            [],
            null));

        var expected = EfProductData.All.OrderByDescending(p => p.Price).Take(5).Select(p => p.Id).ToArray();
        Assert.Equal(expected, page.Items.Select(p => p.Id));
        Assert.Equal(EfProductData.All.Count, page.TotalCount);
    }

    [Fact]
    public async Task NumericFilter_GreaterThan_Translates()
    {
        var expected = EfProductData.All.Count(p => p.Price > 100m);

        var page = await CreateSource().LoadAsync(new GridQuery(
            new PageRequest(1, 10),
            [new SortDescriptor("price")],
            [new FilterDescriptor("price", FilterOperator.GreaterThan, "100")],
            null));

        Assert.Equal(expected, page.TotalCount);
        Assert.All(page.Items, p => Assert.True(p.Price > 100m));
    }

    [Fact]
    public async Task StringContains_IsCaseInsensitive_LikeInMemory()
    {
        var expected = EfProductData.All.Count(p => p.Name.Contains("turbo", StringComparison.OrdinalIgnoreCase));

        var page = await CreateSource().LoadAsync(new GridQuery(
            new PageRequest(1, 10),
            [new SortDescriptor("id")],
            [new FilterDescriptor("name", FilterOperator.Contains, "turbo")],
            null));

        Assert.Equal(expected, page.TotalCount);
        Assert.All(page.Items, p => Assert.Contains("Turbo", p.Name));
    }

    [Fact]
    public async Task StringEquals_IsCaseInsensitive_LikeInMemory()
    {
        var expected = EfProductData.All.Count(p => p.Category.Equals("electronics", StringComparison.OrdinalIgnoreCase));

        var page = await CreateSource().LoadAsync(new GridQuery(
            new PageRequest(1, 50),
            [new SortDescriptor("id")],
            [new FilterDescriptor("category", FilterOperator.Equals, "ELECTRONICS")],
            null));

        Assert.Equal(expected, page.TotalCount);
    }

    [Fact]
    public async Task BoolAndDateOnly_FiltersTranslate()
    {
        var activePage = await CreateSource().LoadAsync(new GridQuery(
            new PageRequest(1, 50),
            [new SortDescriptor("id")],
            [new FilterDescriptor("active", FilterOperator.Equals, "true")],
            null));

        var expectedActive = EfProductData.All.Count(p => p.Active);
        Assert.Equal(expectedActive, activePage.TotalCount);

        var datePage = await CreateSource().LoadAsync(new GridQuery(
            new PageRequest(1, 50),
            [new SortDescriptor("id")],
            [new FilterDescriptor("releasedOn", FilterOperator.GreaterThanOrEqual, "2024-06-01")],
            null));

        var expectedDates = EfProductData.All.Count(p => p.ReleasedOn >= new DateOnly(2024, 6, 1));
        Assert.Equal(expectedDates, datePage.TotalCount);
    }

    [Fact]
    public async Task GlobalSearch_OrsAcrossSearchableColumns()
    {
        var term = "turbo";
        var expected = EfProductData.All.Count(p =>
            p.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            p.Category.Contains(term, StringComparison.OrdinalIgnoreCase));

        var page = await CreateSource().LoadAsync(new GridQuery(
            new PageRequest(1, 50),
            [new SortDescriptor("id")],
            [],
            term));

        Assert.Equal(expected, page.TotalCount);
    }

    [Fact]
    public async Task TotalCount_ReflectsFiltering_NotPaging()
    {
        var page = await CreateSource().LoadAsync(new GridQuery(
            new PageRequest(2, 4),
            [new SortDescriptor("id")],
            [new FilterDescriptor("active", FilterOperator.Equals, "true")],
            null));

        Assert.Equal(EfProductData.All.Count(p => p.Active), page.TotalCount);
        Assert.Equal(4, page.Items.Count);
        Assert.Equal(2, page.Page);
    }

    [Fact]
    public async Task Results_Match_InMemoryPipeline_Parity()
    {
        var gridQuery = new GridQuery(
            new PageRequest(1, 10),
            [new SortDescriptor("category", SortDirection.Ascending), new SortDescriptor("price", SortDirection.Descending)],
            [new FilterDescriptor("active", FilterOperator.Equals, "true"), new FilterDescriptor("stock", FilterOperator.LessThan, "100")],
            null);

        var efPage = await CreateSource().LoadAsync(gridQuery);
        var inMemoryPage = await new InMemoryGridDataSource<EfProduct>(_options, EfProductData.All).LoadAsync(gridQuery);

        Assert.Equal(inMemoryPage.TotalCount, efPage.TotalCount);
        Assert.Equal(
            inMemoryPage.Items.Select(p => p.Id),
            efPage.Items.Select(p => p.Id));
    }

    [Fact]
    public async Task GroupedLoad_NestedCounts_MatchSource()
    {
        var grouped = await CreateSource().LoadGroupedAsync(new GridQuery(
            new PageRequest(1, 10),
            [new SortDescriptor("id")],
            [],
            null,
            ["category"],
            ["category=Electronics"]));

        var expected = EfProductData.All
            .GroupBy(p => p.Category)
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(expected.Count, grouped.TotalGroups);

        var electronics = grouped.Groups.Single(g => g.Value == "Electronics");
        Assert.Equal(expected["Electronics"], electronics.Count);
        Assert.Equal(expected["Electronics"], electronics.Rows.Count);
    }

    [Fact]
    public void Constructor_RequiresExpressionSelectors()
    {
        var options = new GridOptionsBuilder<EfProduct>()
            .WithId("ef-products")
            .AddColumn("name", p => p.Name, c => c.Searchable())   // Func-only: no expression
            .Build();

        Assert.Throws<GridConfigurationException>(() =>
            new EFCoreGridDataSource<EfProduct>(options, _ => ValueTask.FromResult<IQueryable<EfProduct>>(_db.Products)));
    }

    [Fact]
    public async Task UnparseableLiteral_FiltersEverythingOut()
    {
        var page = await CreateSource().LoadAsync(new GridQuery(
            new PageRequest(1, 10),
            [new SortDescriptor("id")],
            [new FilterDescriptor("price", FilterOperator.GreaterThan, "not-a-number")],
            null));

        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task InFilter_TranslatesToOrChain()
    {
        var expected = EfProductData.All.Count(p => p.Category is "Electronics" or "Toys");

        var page = await CreateSource().LoadAsync(new GridQuery(
            new PageRequest(1, 50),
            [new SortDescriptor("id")],
            [new FilterDescriptor("category", FilterOperator.In, """["Electronics","Toys"]""")],
            null));

        Assert.Equal(expected, page.TotalCount);
        Assert.All(page.Items, p => Assert.Contains(p.Category, new List<string> { "Electronics", "Toys" }));
    }

    [Fact]
    public async Task GetValuesAsync_GroupsOnSql_ExcludingOwnFilter()
    {
        var countingSource = (IGridValueCountSource<EfProduct>)CreateSource();
        var column = _options.Columns.Single(c => c.Field == "category");

        var context = new GridQuery(
            new PageRequest(1, 50),
            [],
            [new FilterDescriptor("category", FilterOperator.In, """["Electronics"]"""), new FilterDescriptor("active", FilterOperator.Equals, "true")],
            null);

        var counts = await countingSource.GetValuesAsync(column, context);

        // Own category filter ignored; active=true applied.
        var expected = EfProductData.All
            .Where(p => p.Active)
            .GroupBy(p => p.Category)
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(expected.Count, counts.TotalDistinct);

        foreach (var value in counts.Values)
        {
            Assert.Equal(expected[value.Value], value.Count);
        }
    }
}
