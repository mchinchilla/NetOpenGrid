using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetOpenGrid.Infrastructure.Assets;
using NetOpenGrid.Infrastructure.Runtime;
using Xunit;

namespace NetOpenGrid.Integration.Tests;

/// <summary>Host with the default asset options: the theme comes from the assembly.</summary>
public class ThemeCssLinkTests : IClassFixture<HostFactory>
{
    private readonly HostFactory _factory;

    public ThemeCssLinkTests(HostFactory factory) => _factory = factory;

    [Fact]
    public async Task Document_LinksTheEmbeddedStylesheetWithACacheBustingVersion()
    {
        var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/netgrid/employees");

        var version = EmbeddedGridAssets.Themes["grid"].Version;
        Assert.Contains($"<link rel=\"stylesheet\" href=\"/_netgrid/css/netopengrid-grid.css?v={version}\">", html);
    }

    [Fact]
    public async Task TheStylesheetTheDocumentLinksIsActuallyServed()
    {
        var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/netgrid/employees");

        var href = Regex.Match(html, "<link rel=\"stylesheet\" href=\"([^\"]+)\">").Groups[1].Value;
        Assert.NotEmpty(href);

        var response = await client.GetAsync(href);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(await response.Content.ReadAsStringAsync());
    }
}

/// <summary>Host that opts back in to serving its own stylesheet from wwwroot.</summary>
public sealed class SelfHostedCssFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<NetOpenGridAssetOptions>();
            services.AddSingleton(new NetOpenGridAssetOptions
            {
                AssetPrefix = "/_netgrid",
                CssPath = "/css",
            });
        });
}

public class SelfHostedCssLinkTests : IClassFixture<SelfHostedCssFactory>
{
    private readonly SelfHostedCssFactory _factory;

    public SelfHostedCssLinkTests(SelfHostedCssFactory factory) => _factory = factory;

    [Fact]
    public async Task Document_LinksTheHostStylesheet_WhenCssPathIsSet()
    {
        var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/netgrid/employees");

        Assert.Contains("<link rel=\"stylesheet\" href=\"/css/netopengrid-grid.css\">", html);
        Assert.DoesNotContain("/_netgrid/css/netopengrid-grid.css", html);
    }
}

/// <summary>Host with CssPath set to the web root ("/"), the edge case that used to produce
/// a scheme-relative "//netopengrid-grid.css" URL pointing at an external host.</summary>
public sealed class RootCssFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<NetOpenGridAssetOptions>();
            services.AddSingleton(new NetOpenGridAssetOptions
            {
                AssetPrefix = "/_netgrid",
                CssPath = "/",
            });
        });
}

public class RootCssLinkTests : IClassFixture<RootCssFactory>
{
    private readonly RootCssFactory _factory;

    public RootCssLinkTests(RootCssFactory factory) => _factory = factory;

    [Fact]
    public async Task Document_LinksARootRelativeStylesheet_WhenCssPathIsSlash()
    {
        var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/netgrid/employees");

        Assert.Contains("<link rel=\"stylesheet\" href=\"/netopengrid-grid.css\">", html);
        Assert.DoesNotContain("href=\"//netopengrid-grid.css\"", html);
    }
}
