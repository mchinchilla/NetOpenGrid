using System.Text.RegularExpressions;
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

public class KeyboardAccessibilityTests
{
    private sealed record Item(int Id, string Name);

    [Fact]
    public async Task Sortable_Headers_Bind_Aria_Sort_And_Others_Do_Not()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/items");

        var idHeader = Regex.Match(html, "<th[^>]*data-field=\"id\"[^>]*>").Value;
        var nameHeader = Regex.Match(html, "<th[^>]*data-field=\"name\"[^>]*>").Value;

        Assert.Contains(":aria-sort=\"ariaSort('id')\"", idHeader);
        Assert.DoesNotContain("aria-sort", nameHeader);
    }

    [Fact]
    public async Task Sort_Arrow_Is_Hidden_From_Screen_Readers()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/items");

        Assert.Contains("<span aria-hidden=\"true\" class=\"text-[10px] leading-none opacity-70\" x-show=\"sortDir('id')", html);
    }

    [Fact]
    public async Task Body_And_Head_Carry_The_Keyboard_Handlers()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/items");

        Assert.Contains("id=\"items-body\" @keydown=\"onBodyKeydown($event)\" @focusin=\"onBodyFocusIn($event)\"", html);
        Assert.Contains("@keydown=\"onHeadKeydown($event)\"", html);
        Assert.Contains("[&_td:focus-visible]:outline-2", html);
    }

    [Fact]
    public async Task Client_Runtime_Implements_The_Handlers()
    {
        using var host = await BuildHostAsync();
        var js = await host.GetTestClient().GetStringAsync("/_netgrid/netopengrid.js");

        foreach (var member in new[]
                 {
                     "ariaSort(field)", "onBodyKeydown(event)", "onBodyFocusIn(event)", "onHeadKeydown(event)", "syncRoving(refocus)",
                     "startResize(field, event)", "autoFitColumn(field)", "applyColumnWidths()", "applyHeaderOrder()", "root()"
                 })
        {
            Assert.Contains(member, js);
        }
    }

    [Fact]
    public async Task Every_Column_Header_Gets_A_Resize_Handle()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/items");

        Assert.Equal(2, Regex.Matches(html, "data-resize-handle").Count);
        Assert.Contains("@pointerdown=\"startResize('id', $event)\" @dblclick=\"autoFitColumn('id')\"", html);
        Assert.Contains("[&.is-resized_td]:text-ellipsis", html);
    }

    [Fact]
    public async Task Resize_Handles_Can_Be_Turned_Off()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/fixed");

        Assert.DoesNotContain("data-resize-handle", html);
    }

    [Fact]
    public async Task Client_Runtime_Never_Uses_Alpine_El_As_The_Grid_Root()
    {
        // Inside a method called from an event handler, Alpine's $el is the element the listener
        // sits on (a <th>, a button, the <tbody>): lookups through it silently found nothing.
        using var host = await BuildHostAsync();
        var js = await host.GetTestClient().GetStringAsync("/_netgrid/netopengrid.js");

        Assert.DoesNotMatch(@"this\.\$el\.query", js);
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
                            .AddColumn("id", i => i.Id)
                            .AddColumn("name", i => i.Name, c => c.Sortable(false)),
                            (_, opts) => new InMemoryGridDataSource<Item>(opts, [new(1, "a"), new(2, "b")]))
                        .AddGrid<Item>("fixed", options => options
                            .WithColumnResize(false)
                            .AddColumn("id", i => i.Id),
                            (_, opts) => new InMemoryGridDataSource<Item>(opts, [new(1, "a")]));
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
