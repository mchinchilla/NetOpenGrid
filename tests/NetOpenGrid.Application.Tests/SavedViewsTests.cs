using NetOpenGrid.Application.Builders;
using NetOpenGrid.Application.Json;
using NetOpenGrid.Domain;
using Xunit;

namespace NetOpenGrid.Application.Tests;

public class SavedViewsTests
{
    private sealed record Order(string Status, decimal Total, string City);

    private static GridOptionsBuilder<Order> Builder() => new GridOptionsBuilder<Order>()
        .WithId("orders")
        .AddColumn("status", o => o.Status)
        .AddColumn("total", o => o.Total)
        .AddColumn("city", o => o.City);

    [Fact]
    public void Valid_Views_Are_Kept_With_A_Trimmed_Name_And_Query()
    {
        var options = Builder()
            .AddView("  Delivered  ", "?filter=status:equals:delivered&sort=total:desc")
            .AddView("By city", "groupby=city&cols=city,total,status&hide=status&pageSize=50&q=acme")
            .AddView("In list", "filter=" + Uri.EscapeDataString("status:in:[\"a,b\",\"c\"]"))
            .Build();

        Assert.Equal(3, options.Views.Count);
        Assert.Equal("Delivered", options.Views[0].Name);
        Assert.Equal("filter=status:equals:delivered&sort=total:desc", options.Views[0].Query);
    }

    [Theory]
    [InlineData("page=2", "unknown parameter 'page'")]
    [InlineData("sort=price:desc", "'price' in 'sort'")]
    [InlineData("filter=country:equals:HN", "'country' in 'filter'")]
    [InlineData("groupby=status,region", "'region' in 'groupby'")]
    [InlineData("cols=total,missing", "'missing' in 'cols'")]
    [InlineData("hide=nope", "'nope' in 'hide'")]
    public void Views_Referencing_Unknown_Things_Fail_At_Build(string query, string expected)
    {
        var error = Assert.Throws<GridConfigurationException>(() => Builder().AddView("Broken", query).Build());
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void View_Names_Must_Be_Unique_Ignoring_Case()
    {
        Assert.Throws<GridConfigurationException>(() => Builder()
            .AddView("Pending", "filter=status:equals:pending")
            .AddView("pending", "sort=total")
            .Build());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a name that is definitely much longer than sixty characters in total")]
    public void View_Names_Need_One_To_Sixty_Characters(string name)
    {
        Assert.Throws<GridConfigurationException>(() => Builder().AddView(name, "sort=total").Build());
    }

    [Fact]
    public void Saved_Views_Are_On_By_Default_And_Can_Be_Turned_Off()
    {
        Assert.True(Builder().Build().EnableSavedViews);
        Assert.False(Builder().WithSavedViews(false).Build().EnableSavedViews);
    }

    [Fact]
    public void Json_Grids_Validate_Views_Too()
    {
        var builder = new JsonGridOptionsBuilder()
            .WithId("json")
            .AddColumn("code")
            .AddView("Bad", "sort=nope");

        Assert.Throws<GridConfigurationException>(builder.Build);
    }
}
