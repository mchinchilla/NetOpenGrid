using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetOpenGrid.Application.Builders;
using NetOpenGrid.Application.DataSources;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain;
using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Infrastructure;
using NetOpenGrid.Infrastructure.Endpoints;
using Xunit;

namespace NetOpenGrid.Integration.Tests;

public class RowActionsRenderingTests
{
    private sealed record Item(string Id, string Name, string Link, bool Active, int Units);

    private static readonly Item[] Items =
    [
        new("a&1", "Ana", "/items/a&1", true, 3),
        new("b2", "Luis", "javascript:alert(1)", false, 4)
    ];

    [Fact]
    public async Task Linked_Rows_Carry_An_Encoded_Href_And_Target()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/items/rows?sort=id");
        var rows = Rows(html);

        Assert.Contains("class=\"cursor-pointer ", rows[0]);
        Assert.Contains("data-href=\"/items/a&amp;1\" data-target=\"_top\"", rows[0]);
    }

    [Fact]
    public async Task Unsafe_Row_Links_Are_Dropped()
    {
        using var host = await BuildHostAsync();
        var rows = Rows(await host.GetTestClient().GetStringAsync("/netgrid/items/rows?sort=id"));

        Assert.DoesNotContain("data-href", rows[1]);
        Assert.DoesNotContain("javascript:", rows[1]);
        Assert.DoesNotContain("cursor-pointer", Regex.Match(rows[1], "<tr[^>]*>").Value);
    }

    [Fact]
    public async Task Actions_Cell_Renders_Links_And_Event_Buttons()
    {
        using var host = await BuildHostAsync();
        var rows = Rows(await host.GetTestClient().GetStringAsync("/netgrid/items/rows?sort=id"));

        var actions = Regex.Match(rows[0], "<td data-actions.*?</td>", RegexOptions.Singleline).Value;
        Assert.Contains("<a class=\"cursor-pointer text-sm font-medium text-brand-600", actions);
        Assert.Contains("href=\"/items/a&amp;1\" target=\"_blank\" rel=\"noopener\">Open</a>", actions);
        Assert.Contains("data-key=\"a&amp;1\" @click=\"rowAction('archive', $el.dataset.key)\">Archive</button>", actions);
        Assert.Contains("text-red-600", actions);                        // Danger style
    }

    [Fact]
    public async Task Invisible_Or_Unsafe_Actions_Are_Skipped_Per_Row()
    {
        using var host = await BuildHostAsync();
        var rows = Rows(await host.GetTestClient().GetStringAsync("/netgrid/items/rows?sort=id"));

        var actions = Regex.Match(rows[1], "<td data-actions.*?</td>", RegexOptions.Singleline).Value;
        Assert.DoesNotContain("Archive", actions);                       // visible: only active rows
        Assert.DoesNotContain("<a ", actions);                           // its link is javascript:
    }

    [Fact]
    public async Task Header_Gets_An_Actions_Column()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/items");

        Assert.Matches("<th scope=\"col\" data-actions[^>]*>Actions</th></tr></thead>", html);
    }

    [Fact]
    public async Task Colspans_Count_The_Actions_Column()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();

        var empty = await client.GetStringAsync("/netgrid/items/rows?filter=name:equals:Nobody");
        Assert.Contains("colspan=\"4\"", empty);                        // id, name, units + actions

        var footer = Rows(await client.GetStringAsync("/netgrid/items/rows"))[^1];
        Assert.Contains("agg-total-footer", footer);
        Assert.EndsWith("<td></td></tr>", footer);                       // empty cell under the actions header
    }

    [Fact]
    public async Task Tbody_Wires_Row_Clicks()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/items");

        Assert.Contains("@click=\"onRowClick($event)\" @auxclick=\"onRowClick($event)\"", html);

        var js = await host.GetTestClient().GetStringAsync("/_netgrid/netopengrid.js");
        Assert.Contains("onRowClick(event)", js);
        Assert.Contains("rowAction(action, key)", js);
        Assert.Contains("'netgrid:refresh'", js);
    }

    [Fact]
    public async Task Event_Rows_Carry_The_Event_And_The_Key_Instead_Of_A_Link()
    {
        using var host = await BuildHostAsync();
        var rows = Rows(await host.GetTestClient().GetStringAsync("/netgrid/events/rows?sort=id"));
        var row = Regex.Match(rows[0], "<tr[^>]*>").Value;

        Assert.Contains("class=\"cursor-pointer ", row);
        Assert.Contains("data-row-event=\"details\" data-key=\"a&amp;1\"", row);
        Assert.DoesNotContain("data-href", row);
    }

    [Fact]
    public async Task Row_Clicks_Raise_The_Row_Event()
    {
        using var host = await BuildHostAsync();
        var js = await host.GetTestClient().GetStringAsync("/_netgrid/netopengrid.js");

        Assert.Contains("tr[data-href], tr[data-row-event]", js);
        Assert.Contains("this.rowAction(row.dataset.rowEvent, row.dataset.key)", js);
    }

    [Fact]
    public void A_Row_Event_Needs_A_Row_Key()
    {
        var error = Assert.Throws<GridConfigurationException>(() => new GridOptionsBuilder<Item>().WithId("x")
            .AddColumn("id", i => i.Id)
            .WithRowEvent("details")
            .Build());

        Assert.Contains("WithRowKey", error.Message);
    }

    [Fact]
    public void A_Row_Is_A_Link_Or_An_Event_Not_Both()
    {
        var error = Assert.Throws<GridConfigurationException>(() => new GridOptionsBuilder<Item>().WithId("x")
            .AddColumn("id", i => i.Id)
            .WithRowKey(i => i.Id)
            .WithRowLink(i => i.Link)
            .WithRowEvent("details")
            .Build());

        Assert.Contains("WithRowEvent", error.Message);
    }

    private static string[] Rows(string html) =>
        Regex.Matches(html, "<tr.*?</tr>", RegexOptions.Singleline).Select(m => m.Value).ToArray();

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
                            .AddColumn("name", i => i.Name)
                            .AddColumn("units", i => i.Units, c => c.Aggregate(GridAggregate.Sum))
                            .WithRowKey(i => i.Id)
                            .WithRowLink(i => i.Link, target: "_top")
                            .WithRowActions(a => a
                                .Link("Open", i => i.Link, target: "_blank")
                                .Event("archive", "Archive", RowActionStyle.Danger, visible: i => i.Active)),
                            (_, opts) => new InMemoryGridDataSource<Item>(opts, Items))
                        .AddGrid<Item>("events", options => options
                            .AddColumn("id", i => i.Id)
                            .AddColumn("name", i => i.Name)
                            .WithRowKey(i => i.Id)
                            .WithRowEvent("details"),
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
