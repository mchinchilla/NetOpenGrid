using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetOpenGrid.Application.Builders;
using NetOpenGrid.Application.DataSources;
using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Infrastructure;
using NetOpenGrid.Infrastructure.Endpoints;
using Xunit;

namespace NetOpenGrid.Integration.Tests;

public class AggregateRenderingTests
{
    private sealed record Sale(int Id, string Region, decimal Amount, int Units);

    private static readonly Sale[] Sales =
    [
        new(1, "North", 100m, 3),
        new(2, "North", 50m, 1),
        new(3, "South", 200m, 5),
        new(4, "South", 10m, 2),
        new(5, "West", 40m, 4)
    ];

    [Fact]
    public async Task Rows_Render_Header_And_Footer_Totals_Over_All_Pages()
    {
        using var host = await BuildHostAsync();
        var response = await host.GetTestClient().GetAsync("/netgrid/sales/rows?pageSize=2&sort=id");
        var html = await response.Content.ReadAsStringAsync();

        var rows = Rows(html);
        Assert.Contains("agg-total-header", rows[0]);
        Assert.Contains("agg-total-footer", rows[^1]);
        Assert.Equal(4, rows.Length);                                          // header + 2 data rows + footer

        Assert.Contains("$400.00", rows[^1]);                                   // Sum over the 5 rows, not the page
        Assert.Contains("$80.00", rows[^1]);                                    // Avg
        Assert.Contains(">15<", rows[^1]);                                      // Units sum
        Assert.Contains(">Total<", rows[^1]);
        Assert.Equal("5", response.Headers.GetValues("X-Grid-Total").Single()); // meta counts data rows only
    }

    [Fact]
    public async Task Totals_Follow_The_Filter()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/sales/rows?filter=region:equals:South");

        Assert.Contains("$210.00", Rows(html)[^1]);
    }

    [Fact]
    public async Task No_Totals_When_Nothing_Matches()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/sales/rows?filter=region:equals:Nowhere");

        Assert.DoesNotContain("agg-row", html);
    }

    [Fact]
    public async Task Group_Header_Carries_Subtotals_Inline_And_Expanded_Group_Gets_A_Footer()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync(
            "/netgrid/sales/rows?groupby=region&expand=" + Uri.EscapeDataString("region=South"));

        var rows = Rows(html);
        var southHeader = rows.Single(r => r.Contains("data-group-path=\"region=South\""));
        Assert.Contains("toggleGroup(", southHeader);                           // label and values share the row
        Assert.Contains("$210.00", southHeader);

        var northHeader = rows.Single(r => r.Contains("data-group-path=\"region=North\""));
        Assert.Contains("$150.00", northHeader);                                // collapsed groups show them too
        Assert.DoesNotContain(rows, r => r.Contains("data-group-footer=\"region=North\""));

        var southFooter = rows.Single(r => r.Contains("data-group-footer=\"region=South\""));
        Assert.Contains("Subtotal South", southFooter);
        Assert.Contains("$210.00", southFooter);

        Assert.Contains("$400.00", rows[^1]);                                   // grand total still at the bottom
    }

    [Fact]
    public async Task Aggregate_Cells_Line_Up_With_Their_Columns()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/sales/rows?cols=region,units,id,amount");
        var footer = Rows(html)[^1];

        // Leading non-aggregated column (region) becomes the label cell; then one cell per column.
        Assert.Matches("<td colspan=\"1\"[^>]*>.*Total.*</td><td data-field=\"units\".*<td data-field=\"id\".*<td data-field=\"amount\"", footer);
    }

    [Fact]
    public async Task Shell_First_Render_Includes_The_Totals()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/sales");

        Assert.Contains("agg-total-footer", html);
        Assert.Contains("$400.00", html);
    }

    [Fact]
    public async Task Rows_Without_Aggregate_Rows_Configured_Render_None()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/plain/rows?groupby=region");

        Assert.DoesNotContain("agg-row", html);
        Assert.DoesNotContain("data-agg", html);
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
                        .AddGrid<Sale>("sales", options => Configure(options).WithAggregateRows(GridAggregateRows.All),
                            (_, opts) => new InMemoryGridDataSource<Sale>(opts, Sales))
                        .AddGrid<Sale>("plain", options => Configure(options).WithAggregateRows(GridAggregateRows.None),
                            (_, opts) => new InMemoryGridDataSource<Sale>(opts, Sales));
                });

                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapNetOpenGrid());
                });
            });

        return await builder.StartAsync();
    }

    private static GridOptionsBuilder<Sale> Configure(GridOptionsBuilder<Sale> options) => options
        .AddColumn("id", s => s.Id)
        .AddColumn("region", s => s.Region)
        .AddColumn("amount", s => s.Amount, c => c
            .Aggregate(GridAggregate.Sum | GridAggregate.Avg)
            .Format(v => v.ToString("C2", CultureInfo.GetCultureInfo("en-US"))))
        .AddColumn("units", s => s.Units, c => c.Aggregate(GridAggregate.Sum));
}
