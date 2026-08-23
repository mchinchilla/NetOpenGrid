using NetOpenGrid.Application.Binding;
using NetOpenGrid.Application.Builders;
using NetOpenGrid.Domain.GridQuerying;
using Xunit;

namespace NetOpenGrid.Application.Tests;

public class GridRequestParserTests
{
    [Fact]
    public void ParsesPagingAndClampsToMaximum()
    {
        var normalization = GridRequestParser.Parse(
            Values(("page", ["0"]), ("pageSize", ["9999"])),
            TestGrid.Options(b => b.WithMaxPageSize(100)));

        Assert.Equal(1, normalization.Query.Paging.Page);
        Assert.Equal(100, normalization.Query.Paging.PageSize);
        Assert.NotEmpty(normalization.Warnings);
    }

    [Fact]
    public void ParsesMultiSortWithDirection()
    {
        var normalization = GridRequestParser.Parse(
            Values(("sort", ["city:desc", "name"])),
            TestGrid.Options());

        Assert.Collection(
            normalization.Query.Sorts,
            s =>
            {
                Assert.Equal("city", s.Field);
                Assert.Equal(SortDirection.Descending, s.Direction);
            },
            s =>
            {
                Assert.Equal("name", s.Field);
                Assert.Equal(SortDirection.Ascending, s.Direction);
            });
    }

    [Fact]
    public void DropsUnknownSortFieldWithWarning()
    {
        var normalization = GridRequestParser.Parse(
            Values(("sort", ["hackerField:desc"])),
            TestGrid.Options());

        Assert.Empty(normalization.Query.Sorts);
        Assert.Contains(normalization.Warnings, w => w.Contains("hackerField"));
    }

    [Fact]
    public void ParsesFilterTokensAndSymbols()
    {
        var options = TestGrid.Options();

        var bySymbol = GridRequestParser.Parse(Values(("filter", ["salary:>=60000"])), options);
        var byToken = GridRequestParser.Parse(Values(("filter", ["city:contains:li"])), options);

        Assert.Equal(FilterOperator.GreaterThanOrEqual, bySymbol.Query.Filters.Single().Operator);
        Assert.Equal(FilterOperator.Contains, byToken.Query.Filters.Single().Operator);
    }

    [Fact]
    public void DropsDisallowedOperatorForNumericColumn()
    {
        var normalization = GridRequestParser.Parse(
            Values(("filter", ["salary:contains:60"])),
            TestGrid.Options());

        Assert.Empty(normalization.Query.Filters);
        Assert.Contains(normalization.Warnings, w => w.Contains("salary"));
    }

    [Fact]
    public void AcceptsEmptyOperatorWithoutValue()
    {
        var options = new GridOptionsBuilder<Person>()
            .WithId("people")
            .AddColumn("city", (Person p) => p.City, c => c.AllowedOps(FilterOpSet.IsEmpty | FilterOpSet.IsNotEmpty))
            .Build();

        var normalization = GridRequestParser.Parse(Values(("filter", ["city:is-empty"])), options);

        var filter = Assert.Single(normalization.Query.Filters);
        Assert.Equal(FilterOperator.IsEmpty, filter.Operator);
        Assert.Null(filter.Value);
    }

    [Fact]
    public void TrimsAndTruncatesSearch()
    {
        var normalization = GridRequestParser.Parse(
            Values(("q", [new string('x', 250)])),
            TestGrid.Options());

        Assert.Equal(200, normalization.Query.Search!.Length);
    }

    private static GridRequestValues Values(params (string Key, string[] Values)[] entries) =>
        new(entries.Select(e => new KeyValuePair<string, IReadOnlyList<string>>(e.Key, e.Values)));
}
