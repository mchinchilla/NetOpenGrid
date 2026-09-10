using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetOpenGrid.Application.DataSources;
using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Infrastructure;
using NetOpenGrid.Infrastructure.Endpoints;
using Xunit;

namespace NetOpenGrid.Integration.Tests;

public class EndpointAuthorizationTests
{
    [Fact]
    public async Task Data_Endpoints_Can_Be_Gated_By_The_Host()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();

        var rows = await client.GetAsync("/netgrid/products/rows");
        Assert.Equal(HttpStatusCode.Unauthorized, rows.StatusCode);
    }

    [Fact]
    public async Task Asset_Endpoints_Stay_Anonymous()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();

        var js = await client.GetAsync("/_netgrid/netopengrid.js");
        Assert.Equal(HttpStatusCode.OK, js.StatusCode);
    }

    private static async Task<IHost> BuildHostAsync()
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();

                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthentication("Test")
                        .AddScheme<AuthenticationSchemeOptions, UnauthenticatedTestHandler>("Test", _ => { });
                    services.AddAuthorization();

                    services.AddNetOpenGrid()
                        .AddGrid<Product>("products", options => options
                            .WithTitle("Product catalog")
                            .WithSubtitle("Typed source with computed columns")
                            .WithTheme("grid")
                            .WithDefaultPageSize(10)
                            .WithPageSizeChoices([10, 25, 50])
                            .WithDebounce(250)
                            .EnableRowSelection(p => p.Sku)
                            .AddColumn("sku", p => p.Sku, c => c.Header("SKU").Pinned())
                            .AddColumn("name", p => p.Name, c => c.Searchable())
                            .AddColumn("category", p => p.Category, c => c.Header("Category"))
                            .AddColumn("price", p => p.Price, c => c
                                .Header("Price")
                                .Align(ColumnAlign.End)
                                .Format(v => v.ToString("C2", CultureInfo.GetCultureInfo("en-US"))))
                            .AddColumn("stock", p => p.Stock, c => c.Header("Stock").Align(ColumnAlign.End))
                            .AddColumn("releasedOn", p => p.ReleasedOn, c => c
                                .Header("Released on")
                                .Format(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
                            .AddColumn("available", p => p.Available, c => c.Header("Status").RawCellHtml(p =>
                                $"<span class=\"badge {(p.Available ? "badge-success" : "badge-muted")}\">{(p.Available ? "In stock" : "Sold out")}</span>")),
                            (_, opts) => new InMemoryGridDataSource<Product>(opts, ProductData.All));
                });

                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapNetOpenGrid().RequireAuthorization();
                    });
                });
            });

        var host = await builder.StartAsync();
        return host;
    }

    /// <summary>
    /// Authentication scheme that authenticates nobody: every request is anonymous,
    /// so a <c>.RequireAuthorization()</c>-gated endpoint challenges with 401.
    /// </summary>
    private sealed class UnauthenticatedTestHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.NoResult());
    }
}

file enum Category
{
    Electronics,
    Home,
    Sports,
    Toys,
    Books
}

file sealed record Product(
    string Sku,
    string Name,
    Category Category,
    decimal Price,
    int Stock,
    DateOnly ReleasedOn,
    bool Available);

file static class ProductData
{
    public static readonly IReadOnlyList<Product> All =
    [
        new("SKU-0001", "Turbo Headphones", Category.Electronics, 59.99m, 120, new DateOnly(2023, 3, 1), true),
        new("SKU-0002", "Ultra Keyboard", Category.Electronics, 89.99m, 45, new DateOnly(2022, 11, 15), true),
        new("SKU-0003", "Compact Lamp", Category.Home, 24.99m, 0, new DateOnly(2021, 6, 20), false),
        new("SKU-0004", "Wireless Bottle", Category.Sports, 14.99m, 200, new DateOnly(2023, 8, 9), true),
        new("SKU-0005", "Smart Backpack", Category.Toys, 39.99m, 12, new DateOnly(2020, 1, 30), true),
        new("SKU-0006", "Classic Notebook", Category.Books, 9.99m, 300, new DateOnly(2024, 2, 14), true),
    ];
}
