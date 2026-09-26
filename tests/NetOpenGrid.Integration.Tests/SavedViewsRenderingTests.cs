using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetOpenGrid.Application.DataSources;
using NetOpenGrid.Infrastructure;
using NetOpenGrid.Infrastructure.Endpoints;
using Xunit;

namespace NetOpenGrid.Integration.Tests;

public class SavedViewsRenderingTests
{
    private sealed record Item(string Status, int Total);

    [Fact]
    public async Task Predefined_Views_Reach_The_Client_As_Safe_Json()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/items");

        Assert.Contains("__NETGRID__.views[\"items\"]=[{\"name\":\"Delivered\",\"query\":\"filter=status:equals:delivered\\u0026sort=total:desc\"}", html);
        Assert.DoesNotContain("</script><b>", html);                     // a view name cannot close the script
        Assert.Contains("\\u003C/script\\u003E\\u003Cb\\u003E", html);
    }

    [Fact]
    public async Task Menu_Lists_Predefined_Views_And_Lets_Users_Save_Their_Own()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/items");

        Assert.Contains("aria-controls=\"items-views\" aria-label=\"Saved views\"", html);
        Assert.Contains("x-for=\"view in serverViews\"", html);
        Assert.Contains("x-for=\"view in userViews\"", html);
        Assert.Contains("@submit.prevent=\"saveCurrentView()\"", html);
        Assert.Contains("@click=\"applyView('')\"", html);
    }

    [Fact]
    public async Task Without_User_Views_Only_The_Predefined_Ones_Remain()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/shared");

        Assert.Contains("x-for=\"view in serverViews\"", html);
        Assert.DoesNotContain("saveCurrentView()", html);
    }

    [Fact]
    public async Task No_Menu_When_There_Is_Nothing_To_Show()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/plain");

        Assert.DoesNotContain("-views\"", html);
        Assert.Contains("__NETGRID__.views[\"plain\"]=[]", html);
    }

    [Fact]
    public async Task Client_Runtime_Implements_Views()
    {
        using var host = await BuildHostAsync();
        var js = await host.GetTestClient().GetStringAsync("/_netgrid/netopengrid.js");

        // th[data-actions]: reordering the header must keep the actions column last.
        foreach (var member in new[] { "applyView(query)", "saveCurrentView()", "deleteView(name)", "isActiveView(query)", "seedFromParams(params)", "querySelector('th[data-actions]')" })
        {
            Assert.Contains(member, js);
        }
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
                    services.AddNetOpenGrid()
                        .AddGrid<Item>("items", options => options
                            .AddColumn("status", i => i.Status)
                            .AddColumn("total", i => i.Total)
                            .AddView("Delivered", "filter=status:equals:delivered&sort=total:desc")
                            .AddView("</script><b>", "sort=total"),
                            (_, opts) => new InMemoryGridDataSource<Item>(opts, [new("delivered", 1)]))
                        .AddGrid<Item>("shared", options => options
                            .WithSavedViews(false)
                            .AddColumn("status", i => i.Status)
                            .AddView("Delivered", "filter=status:equals:delivered"),
                            (_, opts) => new InMemoryGridDataSource<Item>(opts, [new("delivered", 1)]))
                        .AddGrid<Item>("plain", options => options
                            .WithSavedViews(false)
                            .AddColumn("status", i => i.Status),
                            (_, opts) => new InMemoryGridDataSource<Item>(opts, [new("delivered", 1)]));
                });

                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapNetOpenGrid());
                });
            });

        return await builder.StartAsync();
    }
}
