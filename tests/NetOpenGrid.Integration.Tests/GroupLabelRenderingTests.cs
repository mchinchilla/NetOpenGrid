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

/// <summary>
/// A group header reads like the cells it groups: the column's format applied to the key (an enum
/// as its display name), not the raw key. The raw key keeps travelling in the group path, so
/// expanding a group still works.
/// </summary>
public class GroupLabelRenderingTests
{
    private enum Status
    {
        Approved,
        Declined,
    }

    private sealed record Payment(int Id, Status Status);

    private static readonly Payment[] Payments = [new(1, Status.Approved), new(2, Status.Approved), new(3, Status.Declined)];

    private static string Display(Status s) => s == Status.Approved ? "Aprobada" : "Rechazada";

    [Fact]
    public async Task Group_Header_Shows_The_Formatted_Value_And_The_Path_Keeps_The_Raw_Key()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync("/netgrid/payments/rows?groupby=status");

        Assert.Contains(">Aprobada<", html);
        Assert.Contains(">Rechazada<", html);
        Assert.DoesNotContain(">Approved<", html);
        Assert.Contains("toggleGroup('status=Approved')", html);
    }

    [Fact]
    public async Task Expanding_A_Formatted_Group_Still_Finds_Its_Rows()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync(
            "/netgrid/payments/rows?groupby=status&expand=" + Uri.EscapeDataString("status=Approved"));

        Assert.Contains("aria-expanded=\"true\"", html);
        Assert.Contains("#1", html);
        Assert.Contains("#2", html);
        Assert.DoesNotContain("#3", html);
    }

    [Fact]
    public async Task A_capped_group_says_how_many_rows_it_shows()
    {
        using var host = await BuildHostAsync();
        var html = await host.GetTestClient().GetStringAsync(
            "/netgrid/payments-capped/rows?groupby=status&expand=" + Uri.EscapeDataString("status=Approved"));

        Assert.Contains("data-group-truncated=\"status=Approved\"", html);
        Assert.Contains("Showing 1 of 2 rows in this group", html);
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
                        .AddGrid<Payment>("payments", options => options
                            .AddColumn("id", p => p.Id, c => c.Format(v => "#" + v))
                            .AddColumn("status", p => p.Status, c => c
                                .Format(Display)
                                .RawCellHtml(p => $"<b>{Display(p.Status)}</b>")),
                            (_, opts) => new InMemoryGridDataSource<Payment>(opts, Payments))
                        .AddGrid<Payment>("payments-capped", options => options
                            .WithMaxGroupRows(1)
                            .AddColumn("id", p => p.Id, c => c.Format(v => "#" + v))
                            .AddColumn("status", p => p.Status, c => c.Format(Display)),
                            (_, opts) => new InMemoryGridDataSource<Payment>(opts, Payments));
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
