using NetOpenGrid.Application.Builders;
using NetOpenGrid.Application.Engine;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain.GridQuerying;
using Xunit;

namespace NetOpenGrid.Application.Tests;

public class GridGrouperTests
{
    private static GridOptions<Person> Options() =>
        new GridOptionsBuilder<Person>()
            .WithId("grouped")
            .AddColumn("name", p => p.Name)
            .AddColumn("city", p => p.City)
            .AddColumn("salary", p => p.Salary)
            .Build();

    private static GridQuery GroupQuery(
        string[] groupBy,
        string[]? expanded = null,
        int page = 1,
        int pageSize = 10) =>
        new(
            new PageRequest(page, pageSize),
            [],
            [],
            null,
            groupBy,
            expanded);

    [Fact]
    public void SingleLevel_GroupsWithCounts_OrderedByValue()
    {
        var rows = TestGrid.People; // Madrid, Lima, Bogota, Lima, null

        var result = GridGrouper<Person>.Group(rows, GroupQuery(["city"]), Options());

        Assert.Equal(4, result.TotalGroups); // null city becomes its own blank bucket
        Assert.Equal("", result.Groups[0].Value);            // nulls sort first (empty key)
        Assert.Equal("Bogota", result.Groups[1].Value);
        Assert.Equal(2, result.Groups.Single(g => g.Value == "Lima").Count);
        Assert.All(result.Groups, g => Assert.Empty(g.Children));
        Assert.All(result.Groups, g => Assert.Empty(g.Rows));
    }

    [Fact]
    public void ExpandedLeaf_ContainsRows()
    {
        var result = GridGrouper<Person>.Group(
            TestGrid.People,
            GroupQuery(["city"], ["city=Lima"]),
            Options());

        var lima = result.Groups.Single(g => g.Value == "Lima");
        Assert.Equal(2, lima.Count);
        Assert.Equal(2, lima.Rows.Count);
        Assert.All(lima.Rows, p => Assert.Equal("Lima", p.City));

        var madrid = result.Groups.Single(g => g.Value == "Madrid");
        Assert.Empty(madrid.Rows);
    }

    [Fact]
    public void NestedGrouping_TwoLevels()
    {
        // Two people in Lima: Bruno (72500) and Diego (90000)
        var result = GridGrouper<Person>.Group(
            TestGrid.People,
            GroupQuery(["city", "salary"], ["city=Lima", "city=Lima|salary=90000"]),
            Options());

        var lima = result.Groups.Single(g => g.Value == "Lima");
        Assert.Equal(2, lima.Children.Count); // two distinct salaries
        Assert.All(lima.Children, c => Assert.Equal(1, c.Count));
        Assert.All(lima.Children, c => Assert.Equal(1, c.Level));
        Assert.All(lima.Children, c => Assert.StartsWith("city=Lima|salary=", c.Path));

        var expandedChild = lima.Children.Single(c => c.Value == "90000");
        Assert.Single(expandedChild.Rows);
        Assert.Equal("Diego Ruiz", expandedChild.Rows[0].Name);

        var collapsedChild = lima.Children.Single(c => c.Value == "72500");
        Assert.Empty(collapsedChild.Rows);
    }

    [Fact]
    public void PagesContainGroups_NotRows()
    {
        var result = GridGrouper<Person>.Group(
            TestGrid.People,
            GroupQuery(["city"], page: 2, pageSize: 2),
            Options());

        Assert.Equal(4, result.TotalGroups);
        Assert.Equal(2, result.Groups.Count);
        Assert.Equal(2, result.Page);
        Assert.Equal(2, result.TotalPages);
    }

    [Fact]
    public void BlankBucket_ForNullKeys()
    {
        var result = GridGrouper<Person>.Group(
            TestGrid.People,
            GroupQuery(["city"], ["city="]),
            Options());

        var blank = result.Groups.Single(g => g.Value.Length == 0);
        Assert.Single(blank.Rows);
        Assert.Equal("Elena Paz", blank.Rows[0].Name);
    }

    [Fact]
    public void GroupOrder_RespectsSortDescriptorOnGroupField()
    {
        var query = new GridQuery(
            new PageRequest(1, 10),
            [new SortDescriptor("city", SortDirection.Descending)],
            [],
            null,
            ["city"]);

        var result = GridGrouper<Person>.Group(TestGrid.People, query, Options());

        Assert.Equal("Madrid", result.Groups[0].Value);
        Assert.Equal("Lima", result.Groups[1].Value);
        Assert.Equal("Bogota", result.Groups[2].Value);
        Assert.Equal("", result.Groups[^1].Value); // blank bucket sorts last when descending
    }

    [Fact]
    public void NoGroupColumns_ReturnsEmpty()
    {
        var result = GridGrouper<Person>.Group(TestGrid.People, GroupQuery([]), Options());

        Assert.Equal(0, result.TotalGroups);
        Assert.Empty(result.Groups);
    }
}
