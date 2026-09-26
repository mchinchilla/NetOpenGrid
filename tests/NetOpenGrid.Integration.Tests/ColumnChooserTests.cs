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

public class ColumnChooserTests
{
    private sealed record Item(int Id, string Name, string City, int Units);

    private static readonly Item[] Items = [new(1, "Ana", "Lima", 3), new(2, "Luis", "Quito", 4)];

    [Fact]
    public async Task Rows_Leave_Out_Hidden_Columns()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/items/rows?hide=name");

        Assert.DoesNotContain("data-field=\"name\"", html);
        Assert.Contains("data-field=\"city\"", html);
        Assert.DoesNotContain("Ana", html);
    }

    [Fact]
    public async Task Shell_Keeps_Hidden_Headers_With_The_Hidden_Attribute()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/items?hide=name");

        var nameHeader = Regex.Match(html, "<th[^>]*data-field=\"name\"[^>]*>").Value;
        var cityHeader = Regex.Match(html, "<th[^>]*data-field=\"city\"[^>]*>").Value;
        Assert.Contains("hidden=\"hidden\"", nameHeader);
        Assert.DoesNotContain("hidden=\"hidden\"", cityHeader);

        var body = html[html.IndexOf("<tbody", StringComparison.Ordinal)..];
        Assert.DoesNotContain("<td data-field=\"name\"", body);
    }

    [Fact]
    public async Task Hiding_Every_Column_Is_Ignored()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/items/rows?hide=id,name,city,units");

        Assert.Contains("data-field=\"name\"", html);
        Assert.Contains("Ana", html);
    }

    [Fact]
    public async Task Totals_Label_Span_Follows_The_Visible_Columns()
    {
        using var host = await BuildHostAsync();
        var all = await host.GetTestClient().GetStringAsync("/netgrid/items/rows");
        var hidden = await host.GetTestClient().GetStringAsync("/netgrid/items/rows?hide=city");

        Assert.Contains("<td colspan=\"3\"", Footer(all));     // id, name, city before units
        Assert.Contains("<td colspan=\"2\"", Footer(hidden));  // city hidden: id, name
    }

    [Fact]
    public async Task Export_Leaves_Out_Hidden_Columns()
    {
        using var host = await BuildHostAsync();
        var csv = await host.GetTestClient().GetStringAsync("/netgrid/items/export?hide=city");

        Assert.Equal("Id,Name,Units", csv.TrimStart('﻿').Split("\r\n")[0]);
    }

    [Fact]
    public async Task Chooser_Menu_Lists_Every_Column()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/items");

        Assert.Contains("aria-controls=\"items-columns\" aria-label=\"Show or hide columns\"", html);
        foreach (var field in new[] { "id", "name", "city", "units" })
        {
            Assert.Contains($"@change=\"toggleColumn('{field}')\"", html);
        }
    }

    [Fact]
    public async Task Chooser_Can_Be_Turned_Off()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/plain");

        Assert.DoesNotContain("toggleColumn(", html);
    }

    private static string Footer(string html) =>
        Regex.Matches(html, "<tr.*?</tr>", RegexOptions.Singleline).Last().Value;

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
                            .AddColumn("city", i => i.City)
                            .AddColumn("units", i => i.Units, c => c.Aggregate(GridAggregate.Sum)),
                            (_, opts) => new InMemoryGridDataSource<Item>(opts, Items))
                        .AddGrid<Item>("plain", options => options
                            .WithColumnChooser(false)
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
