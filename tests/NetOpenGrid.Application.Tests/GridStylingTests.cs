using NetOpenGrid.Application.Json;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain;
using Xunit;

namespace NetOpenGrid.Application.Tests;

public class GridStylingTests
{
    [Theory]
    [InlineData("#0d9488")]
    [InlineData("#0D9488")]
    [InlineData("#fff")]
    [InlineData("#ffff")]
    [InlineData("#0d948880")]
    [InlineData("teal")]
    [InlineData("RebeccaPurple")]
    [InlineData("rgb(13, 148, 136)")]
    [InlineData("rgb(13 148 136 / 50%)")]
    [InlineData("rgba(13,148,136,.5)")]
    [InlineData("hsl(174 84% 32%)")]
    [InlineData("hwb(174 5% 42%)")]
    [InlineData("lab(54.6 -36 -4)")]
    [InlineData("lch(54.6 36 186)")]
    [InlineData("oklab(0.6 -0.1 -0.01)")]
    [InlineData("oklch(0.600 0.118 185)")]
    [InlineData("OKLCH(60% 0.118 185deg)")]
    [InlineData("color(display-p3 0.1 0.55 0.5)")]
    [InlineData("  #0d9488  ")]
    public void CssColor_AcceptsPlainColourSyntax(string color)
    {
        Assert.True(GridCssColor.IsValid(color));
        Assert.Equal(color.Trim(), GridCssColor.Validate(color));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#0d948")]
    [InlineData("#ggg")]
    [InlineData("red;}body{display:none")]
    [InlineData("red}")]
    [InlineData("red{")]
    [InlineData("red</style><script>alert(1)</script>")]
    [InlineData("\"red\"")]
    [InlineData("'red'")]
    [InlineData("url(https://evil.example/x.png)")]
    [InlineData("rgb(1,2,3);background:url(x)")]
    [InlineData("rgb(calc(1),2,3)")]
    [InlineData("var(--x)")]
    [InlineData("expression(alert(1))")]
    [InlineData("red\\;")]
    [InlineData("inherit")]
    [InlineData("initial")]
    [InlineData("te al")]
    public void CssColor_RejectsAnythingThatCouldEscapeTheDeclaration(string? color)
    {
        Assert.False(GridCssColor.IsValid(color));
        Assert.Throws<GridConfigurationException>(() => GridCssColor.Validate(color));
    }

    [Theory]
    [InlineData("/css/grid-overrides.css", "/css/grid-overrides.css")]
    [InlineData("css/grid.css?v=2", "css/grid.css?v=2")]
    [InlineData("https://cdn.example.com/brand.css", "https://cdn.example.com/brand.css")]
    [InlineData("HTTP://cdn.example.com/brand.css", "HTTP://cdn.example.com/brand.css")]
    [InlineData(" /css/a.css ", "/css/a.css")]
    public void StylesheetUrl_AllowsRelativeAndHttp(string url, string expected)
    {
        Assert.Equal(expected, GridUrl.Stylesheet(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/css,body{display:none}")]
    [InlineData("mailto:a@b.c")]
    [InlineData("tel:+50422345678")]
    [InlineData("ftp://example.com/a.css")]
    public void StylesheetUrl_RejectsOtherSchemes(string? url)
    {
        Assert.Null(GridUrl.Stylesheet(url));
        Assert.Throws<GridConfigurationException>(() => GridUrl.ValidateStylesheet(url));
    }

    [Fact]
    public void Builder_DefaultsToNoBrandColourAndNoStylesheets()
    {
        var options = TestGrid.Options();

        Assert.Null(options.BrandColor);
        Assert.Empty(options.Stylesheets);
    }

    [Fact]
    public void Builder_CarriesBrandColourAndStylesheetsInOrder()
    {
        var options = TestGrid.Options(b => b
            .WithBrandColor(" oklch(0.600 0.118 185) ")
            .WithStylesheet("/css/a.css")
            .WithStylesheet("https://cdn.example.com/b.css"));

        Assert.Equal("oklch(0.600 0.118 185)", options.BrandColor);
        Assert.Equal(["/css/a.css", "https://cdn.example.com/b.css"], options.Stylesheets);
    }

    [Fact]
    public void Builder_RejectsAnInjectedBrandColourOrStylesheet()
    {
        Assert.Throws<GridConfigurationException>(() => TestGrid.Options(b => b.WithBrandColor("red;}*{color:red")));
        Assert.Throws<GridConfigurationException>(() => TestGrid.Options(b => b.WithStylesheet("javascript:alert(1)")));
    }

    [Fact]
    public void JsonBuilder_CarriesBrandColourAndStylesheets()
    {
        var options = new JsonGridOptionsBuilder()
            .WithId("json")
            .AddColumn("name")
            .WithBrandColor("#0d9488")
            .WithStylesheet("/css/a.css")
            .Build();

        Assert.Equal("#0d9488", options.BrandColor);
        Assert.Equal(["/css/a.css"], options.Stylesheets);
        Assert.Throws<GridConfigurationException>(() => new JsonGridOptionsBuilder().WithBrandColor("url(x)"));
        Assert.Throws<GridConfigurationException>(() => new JsonGridOptionsBuilder().WithStylesheet("data:text/css,x"));
    }
}
