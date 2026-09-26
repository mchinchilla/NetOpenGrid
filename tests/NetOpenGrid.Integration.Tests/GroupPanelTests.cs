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

public class GroupPanelTests
{
    private sealed record Item(string City, int Units);

    [Fact]
    public async Task Panel_Is_A_Drop_Zone_With_A_Hint()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/items");

        Assert.Contains("data-group-panel role=\"group\" aria-label=\"Group by\"", html);
        Assert.Contains("@dragover.prevent=\"onGroupPanelDragOver($event)\"", html);
        Assert.Contains("@drop.prevent=\"onGroupPanelDrop($event)\"", html);
        Assert.Contains("Drag a column header here to group by it", html);
    }

    [Fact]
    public async Task Chips_Reorder_By_Drag_And_Remove_With_A_Labelled_Button()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/items");

        Assert.Contains("draggable=\"true\" :data-group-chip=\"g\" @dragstart=\"onGroupChipDragStart(g, $event)\"", html);
        Assert.Contains("@drop.prevent.stop=\"onGroupChipDrop(g, $event)\"", html);
        Assert.Contains(":aria-label=\"removeGroupLabel(g)\" @click=\"removeGroupBy(g)\"", html);
    }

    [Fact]
    public async Task Without_The_Panel_The_Plain_Chips_Remain()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/plain");

        Assert.DoesNotContain("data-group-panel", html);
        Assert.Contains("@click=\"removeGroupBy(g)\" class=\"chip\"", html);
    }

    [Fact]
    public async Task Client_Runtime_Implements_The_Panel()
    {
        using var host = await BuildHostAsync();
        var js = await host.GetTestClient().GetStringAsync("/_netgrid/netopengrid.js");

        foreach (var member in new[] { "onGroupPanelDrop(event)", "onGroupChipDrop(target, event)", "moveGroupLevel(field, to)", "startsWith('netgrid-group:')" })
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
                            .AddColumn("city", i => i.City)
                            .AddColumn("units", i => i.Units),
                            (_, opts) => new InMemoryGridDataSource<Item>(opts, [new("Lima", 1)]))
                        .AddGrid<Item>("plain", options => options
                            .WithGroupPanel(false)
                            .AddColumn("city", i => i.City),
                            (_, opts) => new InMemoryGridDataSource<Item>(opts, [new("Lima", 1)]));
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
