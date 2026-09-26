using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetOpenGrid.Application.Builders;
using NetOpenGrid.Application.DataSources;
using NetOpenGrid.Domain;
using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Infrastructure;
using NetOpenGrid.Infrastructure.Endpoints;
using Xunit;

namespace NetOpenGrid.Integration.Tests;

public class VirtualScrollTests
{
    private sealed record Item(int Id, int Units);

    private static readonly Item[] Items = [.. Enumerable.Range(0, 50).Select(i => new Item(i, 1))];

    [Fact]
    public async Task Rows_Carry_Their_Absolute_Index_Across_Blocks()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/items/rows?page=2&pageSize=20&sort=id");

        var indexes = Regex.Matches(html, "data-row=\"(\\d+)\"").Select(m => int.Parse(m.Groups[1].Value)).ToArray();
        Assert.Equal(Enumerable.Range(20, 20), indexes);
    }

    [Fact]
    public async Task Totals_Open_The_First_Block_And_Close_The_Last_One()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();

        var first = await client.GetStringAsync("/netgrid/items/rows?page=1&pageSize=20");
        var middle = await client.GetStringAsync("/netgrid/items/rows?page=2&pageSize=20");
        var last = await client.GetStringAsync("/netgrid/items/rows?page=3&pageSize=20");

        Assert.Contains("agg-total-header", first);
        Assert.DoesNotContain("agg-total-footer", first);
        Assert.DoesNotContain("agg-row", middle);
        Assert.DoesNotContain("agg-total-header", last);
        Assert.Contains("agg-total-footer", last);
        Assert.Contains(">50<", last);                                   // Units sum over every row
    }

    [Fact]
    public async Task Shell_Renders_A_Scroll_Viewport_Instead_Of_The_Pager()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/items");

        Assert.Contains("data-netgrid-card data-virtual @scroll.passive=\"onVirtualScroll()\" style=\"overflow-anchor:none;height:60vh\"", html);
        Assert.Contains("[&_thead_th]:sticky [&_thead_th]:top-0", html);
        Assert.Contains("x-show=\"!isVirtual()\"", html);
        Assert.Contains("__NETGRID__.virtual[\"items\"]={\"blockSize\":20}", html);
        Assert.Equal(20, Regex.Matches(html, "data-row=\"").Count);        // first render is block 0
    }

    [Fact]
    public async Task Classic_Grids_Keep_The_Pager_And_No_Row_Indexes()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/paged");

        Assert.DoesNotContain("data-virtual", html);
        Assert.DoesNotContain("data-row=\"", html);
        Assert.Contains("__NETGRID__.virtual[\"paged\"]=null", html);
    }

    [Fact]
    public async Task Client_Runtime_Implements_The_Virtual_Engine()
    {
        using var host = await BuildHostAsync();
        var js = await host.GetTestClient().GetStringAsync("/_netgrid/netopengrid.js");

        foreach (var member in new[] { "virtualReset(body)", "updateVirtualWindow()", "loadVirtualBlock(block)", "renderVirtual()", "focusVirtualRow(index)", "tr[data-spacer]" })
        {
            Assert.Contains(member.Replace("tr[data-spacer]", "data-spacer"), js);
        }
    }

    [Theory]
    [InlineData(10)]
    [InlineData(5000)]
    public void Block_Size_Is_Validated(int blockSize)
    {
        Assert.Throws<GridConfigurationException>(() => new GridOptionsBuilder<Item>().WithVirtualScroll(blockSize));
    }

    [Fact]
    public void Virtual_Scroll_Makes_The_Block_The_Page()
    {
        var options = new GridOptionsBuilder<Item>()
            .WithId("v")
            .WithMaxPageSize(50)
            .AddColumn("id", i => i.Id)
            .WithVirtualScroll(blockSize: 200)
            .Build();

        Assert.Equal(200, options.DefaultPageSize);
        Assert.Equal(200, options.MaxPageSize);
        Assert.Equal([200], options.PageSizeChoices);
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
                            .WithAggregateRows(GridAggregateRows.Header | GridAggregateRows.Footer)
                            .AddColumn("id", i => i.Id)
                            .AddColumn("units", i => i.Units, c => c.Aggregate(GridAggregate.Sum))
                            .WithVirtualScroll(blockSize: 20, height: "60vh"),
                            (_, opts) => new InMemoryGridDataSource<Item>(opts, Items))
                        .AddGrid<Item>("paged", options => options
                            .AddColumn("id", i => i.Id),
                            (_, opts) => new InMemoryGridDataSource<Item>(opts, Items));
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
