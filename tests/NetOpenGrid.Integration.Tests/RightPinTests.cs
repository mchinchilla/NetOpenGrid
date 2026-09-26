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

public class RightPinTests
{
    private sealed record Item(string Code, string Name, string City, int Units);

    [Fact]
    public async Task Right_Pinned_Columns_Are_Shown_Last_And_Stick_To_The_Right()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/items");

        // "units" is configured second but pinned right: it moves to the end (before actions).
        var headerFields = Regex.Matches(html, "<th[^>]*data-field=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(["code", "name", "city", "units"], headerFields);

        var unitsHeader = Regex.Match(html, "<th[^>]*data-field=\"units\"[^>]*>").Value;
        Assert.Contains("data-pin-right=\"units\" style=\"right:0\"", unitsHeader);
        Assert.Contains("sticky z-30 relative border-l", unitsHeader);

        var body = html[html.IndexOf("<tbody", StringComparison.Ordinal)..];
        var rowFields = Regex.Matches(Regex.Match(body, "<tr.*?</tr>", RegexOptions.Singleline).Value, "<td data-field=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(headerFields, rowFields);
        Assert.Contains("<td data-field=\"units\" data-pin-right=\"units\" style=\"right:0\" class=\"sticky z-10 border-l", body);

        Assert.Contains("{\"field\":\"units\",\"header\":\"Units\",", html);
        Assert.Contains("\"pin\":\"right\"", html);
        Assert.Contains("\"pin\":\"left\"", html);
    }

    [Fact]
    public async Task Pinned_Actions_Column_Sticks_To_The_Right()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/items");

        Assert.Contains("<th scope=\"col\" data-actions data-pin-right=\"__actions\" style=\"right:0\" class=\"sticky z-30", html);
        Assert.Contains("<td data-actions data-pin-right=\"__actions\" style=\"right:0\" class=\"sticky z-10", html);
    }

    [Fact]
    public async Task Export_Follows_The_Display_Order()
    {
        using var host = await BuildHostAsync();
        var csv = await host.GetTestClient().GetStringAsync("/netgrid/items/export");

        Assert.Equal("Code,Name,City,Units", csv.TrimStart('﻿').Split("\r\n")[0]);
    }

    [Fact]
    public async Task Pin_Button_Cycles_With_A_Label_For_The_Next_Step()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/items");
        var js = await host.GetTestClient().GetStringAsync("/_netgrid/netopengrid.js");

        Assert.Contains(":class=\"pinButtonClass('city')\" @click=\"togglePin('city')\" :aria-label=\"pinLabel('city')\" aria-label=\"Pin column City to the left\"", html);
        foreach (var member in new[] { "effectiveColumnOrder()", "pinSide(field)", "data-pin-right", "'-scale-x-100'" })
        {
            Assert.Contains(member.Trim('\''), js);
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
                            .AddColumn("code", i => i.Code, c => c.Pinned())
                            .AddColumn("units", i => i.Units, c => c.PinnedRight())
                            .AddColumn("name", i => i.Name)
                            .AddColumn("city", i => i.City)
                            .WithRowActions(a => a.Link("Open", i => $"/items/{i.Code}").Pinned()),
                            (_, opts) => new InMemoryGridDataSource<Item>(opts, [new("A1", "Ana", "Lima", 3)]));
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
