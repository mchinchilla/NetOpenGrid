using NetOpenGrid.Application.Builders;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain;
using Xunit;

namespace NetOpenGrid.Application.Tests;

public class RowActionsTests
{
    private sealed record Row(string Id);

    [Theory]
    [InlineData("/products/1", "/products/1")]
    [InlineData("products/1?x=a:b", "products/1?x=a:b")]
    [InlineData("  https://example.com/a  ", "https://example.com/a")]
    [InlineData("HTTP://example.com", "HTTP://example.com")]
    [InlineData("mailto:a@b.c", "mailto:a@b.c")]
    [InlineData("#details", "#details")]
    [InlineData("javascript:alert(1)", null)]
    [InlineData(" JavaScript:alert(1)", null)]
    [InlineData("java\tscript:alert(1)", null)]
    [InlineData("data:text/html,<b>x</b>", null)]
    [InlineData("vbscript:msgbox", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Safe_Url_Keeps_Relative_And_Web_Schemes_Only(string? url, string? expected)
    {
        Assert.Equal(expected, GridUrl.Safe(url));
    }

    [Fact]
    public void Event_Actions_Need_A_Row_Key()
    {
        var error = Assert.Throws<GridConfigurationException>(() => new GridOptionsBuilder<Row>()
            .WithId("rows")
            .AddColumn("id", r => r.Id)
            .WithRowActions(a => a.Event("archive", "Archive"))
            .Build());

        Assert.Contains("WithRowKey", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void With_Row_Key_Event_Actions_Build()
    {
        var options = new GridOptionsBuilder<Row>()
            .WithId("rows")
            .AddColumn("id", r => r.Id)
            .WithRowKey(r => r.Id)
            .WithRowActions(a => a
                .Link("Open", r => $"/rows/{r.Id}")
                .Event("archive", "Archive", RowActionStyle.Danger)
                .Header("Do"))
            .Build();

        Assert.Equal(2, options.RowActions.Count);
        Assert.Equal("Do", options.RowActionsHeader);
        Assert.False(options.EnableRowSelection);   // a row key alone does not turn selection on
    }

    [Fact]
    public void Link_Only_Actions_Do_Not_Need_A_Key()
    {
        var options = new GridOptionsBuilder<Row>()
            .WithId("rows")
            .AddColumn("id", r => r.Id)
            .WithRowActions(a => a.Link("Open", r => $"/rows/{r.Id}"))
            .Build();

        Assert.True(options.HasRowActions);
    }

    [Fact]
    public void Duplicate_Event_Names_Are_Rejected()
    {
        Assert.Throws<GridConfigurationException>(() => new GridOptionsBuilder<Row>()
            .WithRowActions(a => a.Event("archive", "Archive").Event("archive", "Again")));
    }
}
