using System.Globalization;
using System.Text.Json;
using NetOpenGrid.Application.Builders;
using NetOpenGrid.Application.DataSources;
using NetOpenGrid.Application.Engine;
using NetOpenGrid.Application.Json;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain;
using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Domain.GridQuerying;
using Xunit;

namespace NetOpenGrid.Application.Tests;

public class AggregateTests
{
    private sealed record Sale(string Region, string Rep, decimal Amount, int Units, double? Rating);

    private static readonly Sale[] Sales =
    [
        new("North", "Ana", 100.50m, 3, 4.5),
        new("North", "Luis", 49.50m, 1, null),
        new("South", "Eva", 200m, 5, 3.0),
        new("South", "Eva", 10m, 2, 5.0),
        new("West", "Tom", 40m, 4, 4.0)
    ];

    private const GridAggregate All = GridAggregate.Sum | GridAggregate.Avg | GridAggregate.Min | GridAggregate.Max;

    private static GridOptions<Sale> Options(GridAggregateRows rows = GridAggregateRows.All) =>
        new GridOptionsBuilder<Sale>()
            .WithId("sales")
            .WithAggregateRows(rows)
            .AddColumn("region", s => s.Region)
            .AddColumn("rep", s => s.Rep)
            .AddColumn("amount", s => s.Amount, c => c
                .Aggregate(All)
                .Format(v => v.ToString("C2", CultureInfo.GetCultureInfo("en-US"))))
            .AddColumn("units", s => s.Units, c => c.Aggregate(GridAggregate.Sum | GridAggregate.Avg))
            .AddColumn("rating", s => s.Rating, c => c.Aggregate(GridAggregate.Avg | GridAggregate.Max))
            .Build();

    private static GridQuery Query(params FilterDescriptor[] filters) => new(new PageRequest(1, 1), [], filters);

    [Fact]
    public void Aggregator_Computes_Sum_Avg_Min_Max_Skipping_Nulls()
    {
        var options = Options();
        var totals = GridAggregator.Compute(Sales, options.AggregateColumns);

        Assert.Equal(new(400m, 80m, 10m, 200m), totals.For("amount"));
        Assert.Equal(15m, totals.For("units")!.Sum);
        Assert.Equal(3m, totals.For("units")!.Avg);
        Assert.Null(totals.For("units")!.Min);                 // not requested

        var rating = totals.For("rating")!;
        Assert.Equal(4.125m, rating.Avg);                      // the null rating is skipped, like SQL AVG
        Assert.Equal(5m, rating.Max);
    }

    [Fact]
    public void Aggregator_Over_No_Rows_Yields_Nulls()
    {
        var totals = GridAggregator.Compute(Array.Empty<Sale>(), Options().AggregateColumns);

        Assert.Equal(new(null, null, null, null), totals.For("amount"));
    }

    [Fact]
    public async Task InMemory_Source_Aggregates_The_Whole_Filtered_Set_Not_The_Page()
    {
        var options = Options();
        var source = new InMemoryGridDataSource<Sale>(options, Sales);

        var totals = await source.GetAggregatesAsync(Query(new FilterDescriptor("region", FilterOperator.Equals, "South")));

        Assert.Equal(210m, totals.For("amount")!.Sum);        // both South rows, whatever the page
        Assert.Equal(7m, totals.For("units")!.Sum);
    }

    [Fact]
    public void Default_Format_Reuses_The_Column_Formatter()
    {
        var amount = Options().Columns.Single(c => c.Field == "amount");

        Assert.Equal("$1,234.50", amount.AggregateFormat!(GridAggregate.Sum, 1234.5m));
        Assert.Equal("$80.33", amount.AggregateFormat!(GridAggregate.Avg, 80.3333m));
    }

    [Fact]
    public void Avg_Of_An_Integral_Column_Keeps_Two_Decimals()
    {
        var units = Options().Columns.Single(c => c.Field == "units");

        Assert.Equal("15", units.AggregateFormat!(GridAggregate.Sum, 15m));
        Assert.Equal("2.67", units.AggregateFormat!(GridAggregate.Avg, 2.6666m));
    }

    [Fact]
    public void A_Sum_That_Overflows_The_Column_Type_Still_Formats()
    {
        var units = Options().Columns.Single(c => c.Field == "units");

        Assert.Equal("3000000000", units.AggregateFormat!(GridAggregate.Sum, 3_000_000_000m));
    }

    [Fact]
    public void Custom_Aggregate_Format_Wins()
    {
        var options = new GridOptionsBuilder<Sale>()
            .WithId("custom")
            .AddColumn("units", s => s.Units, c => c
                .Aggregate(GridAggregate.Sum)
                .AggregateFormat((fn, v) => $"{fn}={v}"))
            .Build();

        Assert.Equal("Sum=7", options.Columns[0].AggregateFormat!(GridAggregate.Sum, 7m));
    }

    [Fact]
    public void Aggregate_On_A_Non_Numeric_Column_Fails_At_Configuration()
    {
        var error = Assert.Throws<GridConfigurationException>(() => new GridOptionsBuilder<Sale>()
            .WithId("bad")
            .AddColumn("rep", s => s.Rep, c => c.Aggregate(GridAggregate.Sum)));
        Assert.Contains("rep", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Grouper_Attaches_Subtotals_To_Every_Group_Level()
    {
        var query = new GridQuery(new PageRequest(1, 10), [], [], null, ["region", "rep"], ["region=South"]);

        var result = GridGrouper<Sale>.Group(Sales, query, Options());

        var south = result.Groups.Single(g => g.Value == "South");
        Assert.Equal(210m, south.Aggregates.For("amount")!.Sum);
        Assert.Equal(10m, south.Aggregates.For("amount")!.Min);

        var eva = Assert.Single(south.Children);
        Assert.Equal(210m, eva.Aggregates.For("amount")!.Sum);

        var north = result.Groups.Single(g => g.Value == "North");   // collapsed groups carry subtotals too
        Assert.Equal(150m, north.Aggregates.For("amount")!.Sum);
    }

    [Fact]
    public void Grouper_Skips_Subtotals_When_No_Group_Row_Shows_Them()
    {
        var query = new GridQuery(new PageRequest(1, 10), [], [], null, ["region"]);

        var result = GridGrouper<Sale>.Group(Sales, query, Options(GridAggregateRows.Footer));

        Assert.All(result.Groups, g => Assert.True(g.Aggregates.IsEmpty));
    }

    [Fact]
    public void Json_Columns_Aggregate_Numeric_Properties()
    {
        using var doc = JsonDocument.Parse("""[{"n":1.5},{"n":"x"},{"n":2.5},{}]""");
        var options = new JsonGridOptionsBuilder()
            .WithId("json")
            .AddColumn("n", c => c.Aggregate(GridAggregate.Sum | GridAggregate.Avg))
            .Build();

        var totals = GridAggregator.Compute(doc.RootElement.EnumerateArray().ToArray(), options.AggregateColumns);

        Assert.Equal(4m, totals.For("n")!.Sum);
        Assert.Equal(2m, totals.For("n")!.Avg);
    }
}
