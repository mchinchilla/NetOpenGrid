using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using NetOpenGrid.Infrastructure.Assets;
using Xunit;

namespace NetOpenGrid.Integration.Tests;

public class ThemeCssEndpointTests : IClassFixture<HostFactory>
{
    private readonly HostFactory _factory;

    public ThemeCssEndpointTests(HostFactory factory) => _factory = factory;

    [Theory]
    [InlineData("grid")]
    [InlineData("midnight")]
    [InlineData("tekium")]
    public async Task ThemeCss_IsServedFromTheEmbeddedAssets(string theme)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/_netgrid/css/netopengrid-{theme}.css");

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);

        var css = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(css);
        Assert.Equal(EmbeddedGridAssets.Themes[theme].Bytes.Length, response.Content.Headers.ContentLength);
    }

    [Fact]
    public async Task ThemeCss_IsImmutablyCacheable()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/_netgrid/css/netopengrid-grid.css");

        response.EnsureSuccessStatusCode();
        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task ThemeCss_UnknownTheme_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/_netgrid/css/netopengrid-doesnotexist.css");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
