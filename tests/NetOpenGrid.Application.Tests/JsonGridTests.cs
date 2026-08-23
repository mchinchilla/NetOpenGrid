using System.Text.Json;
using NetOpenGrid.Application.Json;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain;
using NetOpenGrid.Domain.GridQuerying;
using Xunit;

namespace NetOpenGrid.Application.Tests;

public class JsonGridTests
{
    private static GridOptions<JsonElement> Options() => new JsonGridOptionsBuilder()
        .WithId("rows")
        .AddColumn("name", c => c.Searchable())
        .AddColumn("amount")
        .AddColumn("status")
        .Build();

    private const string Payload = """
        [
          {"name": "alpha", "amount": 10, "status": "open"},
          {"name": "beta", "amount": 30.5, "status": "closed"},
          {"name": "gamma", "amount": 20, "status": "open"},
          {"name": null, "status": "open"}
        ]
        """;

    private static JsonGridDataSource Source() => new(Options(), Payload);

    internal static JsonGridDataSource SourcePublic() => Source();

    [Fact]
    public async Task SortsNumbersDescending_AndHandlesMissingProperty()
    {
        var page = await Source().LoadAsync(new GridQuery(
            new PageRequest(1, 10),
            [new SortDescriptor("amount", SortDirection.Descending)],
            [],
            null));

        Assert.Equal(4, page.TotalCount);
        Assert.Equal(30.5m, page.Items[0].GetProperty("amount").GetDecimal());
        Assert.Equal(JsonValueKind.Null, page.Items[^1].GetProperty("name").ValueKind);
    }

    [Fact]
    public async Task FiltersByStringEquality_CaseInsensitive()
    {
        var page = await Source().LoadAsync(new GridQuery(
            new PageRequest(1, 10),
            [],
            [new FilterDescriptor("status", FilterOperator.Equals, "OPEN")],
            null));

        Assert.Equal(3, page.TotalCount);
    }

    [Fact]
    public async Task NumericComparisonFilter()
    {
        var page = await Source().LoadAsync(new GridQuery(
            new PageRequest(1, 10),
            [],
            [new FilterDescriptor("amount", FilterOperator.GreaterThan, "15")],
            null));

        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task GlobalSearchOnJsonStringColumn()
    {
        var page = await Source().LoadAsync(new GridQuery(
            new PageRequest(1, 10),
            [],
            [],
            "gam"));

        var row = Assert.Single(page.Items);
        Assert.Equal("gamma", row.GetProperty("name").GetString());
    }

    [Fact]
    public async Task IsEmptyMatchesNullAndMissing()
    {
        var options = new JsonGridOptionsBuilder()
            .WithId("rows")
            .AddColumn("name", c => c.AllowedOps(Domain.GridQuerying.FilterOpSet.IsEmpty | Domain.GridQuerying.FilterOpSet.IsNotEmpty))
            .Build();

        var page = await new JsonGridDataSource(options, Payload).LoadAsync(new GridQuery(
            new PageRequest(1, 10),
            [],
            [new FilterDescriptor("name", FilterOperator.IsEmpty, null)],
            null));

        var row = Assert.Single(page.Items);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("name").ValueKind);
    }

    [Fact]
    public async Task FormatsCellsFromPropertyValues()
    {
        var options = Options();
        var column = options.Columns.Single(c => c.Field == "amount");

        var page = await Source().LoadAsync(GridQuery.Empty);

        Assert.Equal("30.5", column.Format(page.Items.Single(i => i.TryGetProperty("name", out var n) && n.GetString() == "beta")));
    }

    [Fact]
    public void RejectsNonArrayRoot() =>
        Assert.Throws<GridException>(() => new JsonGridDataSource(Options(), "{\"name\":\"x\"}"));
}
