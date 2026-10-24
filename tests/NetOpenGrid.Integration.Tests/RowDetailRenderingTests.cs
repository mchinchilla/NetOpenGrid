using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetOpenGrid.Application.DataSources;
using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Infrastructure;
using NetOpenGrid.Infrastructure.Endpoints;
using Xunit;

namespace NetOpenGrid.Integration.Tests;

public class RowDetailRenderingTests
{
    private sealed record Item(string Id, string Name, string Group, int Units, string? Detail);

    private static readonly Item[] Items =
    [
        new("a&1", "Ana", "x", 3, "/calls/a&1/detail"),
        new("b2", "Luis", "x", 4, null),
        new("c3", "Eva", "y", 5, "javascript:alert(1)")
    ];

    [Fact]
    public async Task Rows_Carry_Only_The_Detail_Url_And_A_Collapsed_Toggle()
    {
        using var host = await BuildHostAsync();
        var row = Rows(await host.GetTestClient().GetStringAsync("/netgrid/items/rows?sort=id"))[1];

        Assert.Matches("<tr class=\"cursor-pointer [^\"]*\" data-id=\"a&amp;1\" data-detail=\"/calls/a&amp;1/detail\">", row);
        Assert.Contains(
            "<td data-pin=\"__detail\" style=\"left:0\" class=\"ng-detail-cell\"><button type=\"button\" class=\"ng-detail-toggle\" data-detail-toggle aria-expanded=\"false\" aria-label=\"Show or hide detail\"></button></td>",
            row);
        Assert.DoesNotContain("<svg", row);                              // the chevron is CSS, not per-row markup
    }

    [Fact]
    public async Task Rows_Without_A_Safe_Url_Keep_An_Empty_Expander_Cell()
    {
        using var host = await BuildHostAsync();
        var rows = Rows(await host.GetTestClient().GetStringAsync("/netgrid/items/rows?sort=id"));

        foreach (var row in new[] { rows[2], rows[3] })                  // null, then javascript:
        {
            Assert.DoesNotContain("data-detail", row);
            Assert.DoesNotContain("javascript:", row);
            Assert.DoesNotContain("cursor-pointer", Regex.Match(row, "<tr[^>]*>").Value);
            Assert.Contains("<td data-pin=\"__detail\" style=\"left:0\" class=\"ng-detail-cell\"></td>", row);
        }
    }

    [Fact]
    public async Task Header_Gets_A_Pinned_Expander_Column_First()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/items");

        Assert.Matches("<thead[^>]*><tr><th scope=\"col\" data-pin=\"__detail\"[^>]*><span class=\"sr-only\">Detail</span></th><th scope=\"col\" data-pin=\"__select\"", html);
    }

    [Fact]
    public async Task Colspans_And_Aggregate_Rows_Count_The_Expander_Column()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();

        var empty = await client.GetStringAsync("/netgrid/items/rows?filter=name:equals:Nobody");
        Assert.Contains("colspan=\"6\"", empty);                        // expander, select, id, name, group, units

        // The label spans the expander, the selection and the leading non-aggregated columns.
        var header = Rows(await client.GetStringAsync("/netgrid/items/rows"))[0];
        Assert.Contains("agg-total-header", header);
        Assert.Contains("<td colspan=\"5\"", header);

        var grouped = await client.GetStringAsync("/netgrid/items/rows?groupby=group&expand=x");
        Assert.Contains("<td colspan=\"6\"", Rows(grouped).First(r => r.Contains("group-header")));
    }

    [Fact]
    public async Task Grids_Without_A_Detail_Render_No_Expander()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/plain");

        Assert.DoesNotContain("__detail", html);
        Assert.DoesNotContain("data-detail", html);
    }

    [Fact]
    public async Task Runtime_Fetches_Details_Lazily_And_Caches_Them()
    {
        using var host = await BuildHostAsync();
        var js = await host.GetTestClient().GetStringAsync("/_netgrid/netopengrid.js");

        Assert.Contains("tr[data-href], tr[data-row-event], tr[data-detail]", js);
        Assert.Contains("toggleDetail(row)", js);
        Assert.Contains("restoreDetails(prune)", js);
        Assert.Contains("'X-NetGrid-Detail': 'true'", js);
        Assert.Contains("const DETAIL_CACHE = 50;", js);
    }

    [Fact]
    public async Task Themes_Ship_The_Detail_Classes()
    {
        using var host = await BuildHostAsync();
        var css = await host.GetTestClient().GetStringAsync("/_netgrid/css/netopengrid-grid.css");

        foreach (var cls in new[] { ".ng-detail-toggle", ".ng-detail-row", ".ng-detail-panel", ".ng-detail-card", ".ng-detail-title", ".ng-detail-fields", ".ng-detail-table", ".ng-num" })
        {
            Assert.Contains(cls, css);
        }
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
                            .AddColumn("group", i => i.Group)
                            .AddColumn("units", i => i.Units, c => c.Aggregate(GridAggregate.Sum))
                            .WithAggregateRows(GridAggregateRows.Header)
                            .EnableRowSelection(i => i.Id)
                            .WithRowDetail(i => i.Detail),
                            (_, opts) => new InMemoryGridDataSource<Item>(opts, Items))
                        .AddGrid<Item>("plain", options => options
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
