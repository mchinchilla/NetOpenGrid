using NetOpenGrid.Application.Builders;
using NetOpenGrid.Application.DataSources;
using NetOpenGrid.Domain.GridQuerying;
using Xunit;

namespace NetOpenGrid.Application.Tests;

public class InMemoryPipelineTests
{
    [Fact]
    public async Task FiltersSortsAndPaginates()
    {
        var source = new InMemoryGridDataSource<Person>(TestGrid.Options(), TestGrid.People);

        var page = await source.LoadAsync(new GridQuery(
            new PageRequest(1, 2),
            [new SortDescriptor("salary", SortDirection.Descending)],
            [new FilterDescriptor("city", FilterOperator.Equals, "lima")],
            null));

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal("Diego Ruiz", page.Items[0].Name);
        Assert.Equal("Bruno Diaz", page.Items[1].Name);
        Assert.Equal(1, page.TotalPages);
    }

    [Fact]
    public async Task GlobalSearchMatchesAnySearchableColumn()
    {
        var source = new InMemoryGridDataSource<Person>(TestGrid.Options(), TestGrid.People);

        var page = await source.LoadAsync(new GridQuery(
            new PageRequest(1, 10),
            [],
            [],
            "bruno"));

        var row = Assert.Single(page.Items);
        Assert.Equal("Bruno Diaz", row.Name);
    }

    [Fact]
    public async Task StableSortPreservesOriginalOrderForTies()
    {
        var people = new List<Person>
        {
            new(1, "A", "X", 50m, default),
            new(2, "B", "Y", 50m, default),
            new(3, "C", "Z", 50m, default),
            new(4, "D", "W", 50m, default)
        };

        var source = new InMemoryGridDataSource<Person>(TestGrid.Options(), people);

        var page = await source.LoadAsync(new GridQuery(
            new PageRequest(1, 10),
            [new SortDescriptor("salary")],
            [],
            null));

        Assert.Equal([1, 2, 3, 4], page.Items.Select(p => p.Id));
    }

    [Fact]
    public async Task NullKeysSortBelowValues()
    {
        var source = new InMemoryGridDataSource<Person>(TestGrid.Options(), TestGrid.People);

        var page = await source.LoadAsync(new GridQuery(
            new PageRequest(1, 10),
            [new SortDescriptor("city")],
            [],
            null));

        // Elena has a null city and must come first.
        Assert.Equal("Elena Paz", page.Items[0].Name);
    }

    [Fact]
    public async Task EmptyResultKeepsRequestedPage()
    {
        var source = new InMemoryGridDataSource<Person>(TestGrid.Options(), TestGrid.People);

        var page = await source.LoadAsync(new GridQuery(
            new PageRequest(3, 10),
            [],
            [new FilterDescriptor("name", FilterOperator.Equals, "nope")],
            null));

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(3, page.Page);
    }

    [Fact]
    public async Task BoolColumn_FiltersByEquals()
    {
        var options = new GridOptionsBuilder<Person>()
            .WithId("people")
            .AddColumn("name", p => p.Name)
            .AddColumn("vip", p => p.Id is 2 or 4, c => c.Header("Vip"))
            .Build();

        var source = new InMemoryGridDataSource<Person>(options, TestGrid.People);

        var page = await source.LoadAsync(new GridQuery(
            new PageRequest(1, 10),
            [],
            [new FilterDescriptor("vip", FilterOperator.Equals, "true")],
            null));

        Assert.Equal(2, page.TotalCount);
        Assert.All(page.Items, p => Assert.True(p.Id is 2 or 4));
    }

    [Fact]
    public async Task InFilter_String_IsCaseInsensitive()
    {
        var source = new InMemoryGridDataSource<Person>(TestGrid.Options(), TestGrid.People);

        var page = await source.LoadAsync(new GridQuery(
            new PageRequest(1, 10),
            [new SortDescriptor("id")],
            [new FilterDescriptor("city", FilterOperator.In, """["LIMA","bogota"]""")],
            null));

        Assert.Equal(3, page.TotalCount);
        Assert.All(page.Items, p => Assert.Contains(p.City, new List<string> { "Lima", "Bogota" }));
    }

    [Fact]
    public async Task InFilter_Numeric_MatchesAny()
    {
        var source = new InMemoryGridDataSource<Person>(TestGrid.Options(), TestGrid.People);

        var page = await source.LoadAsync(new GridQuery(
            new PageRequest(1, 10),
            [new SortDescriptor("id")],
            [new FilterDescriptor("salary", FilterOperator.In, """["55000","90000"]""")],
            null));

        Assert.Equal([1, 4], page.Items.Select(p => p.Id));
    }

    [Fact]
    public async Task ValueCounts_ExcludeOwnFilter_ButKeepContext()
    {
        var source = new InMemoryGridDataSource<Person>(TestGrid.Options(), TestGrid.People);
        var column = TestGrid.Options().Columns.Single(c => c.Field == "city");

        var context = new GridQuery(
            new PageRequest(1, 10),
            [],
            [new FilterDescriptor("city", FilterOperator.In, """["Lima"]"""), new FilterDescriptor("salary", FilterOperator.GreaterThan, "60000")],
            null);

        var counts = await source.GetValuesAsync(column, context);

        // Own city filter ignored; salary filter applied → only Lima rows above 60k.
        var lima = Assert.Single(counts.Values);
        Assert.Equal("Lima", lima.Value);
        Assert.Equal(2, lima.Count);
        Assert.Equal(1, counts.TotalDistinct);
    }
}
