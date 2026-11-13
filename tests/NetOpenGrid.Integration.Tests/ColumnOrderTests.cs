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

/// <summary>
/// A column order (cols=) that lists only some columns, or fields that do not exist, must still
/// leave the header and the cells in the same order, on the server and on the client.
/// </summary>
public class ColumnOrderTests
{
    private sealed record Provider(string Code, string CompanyName, string Secret, decimal CurrentBalance, string Aprobacion, string Estado);

    // "code" is pinned left and "actions" (a plain host field, not the library's actions column)
    // is pinned right, so the default display order is the declaration order.
    private static readonly string[] DefaultOrder = ["code", "companyName", "currentBalance", "aprobacion", "estado", "actions"];

    [Theory]
    // Incomplete: an order saved before the grid gained "aprobacion" and "estado".
    [InlineData("cols=companyName,code,currentBalance,actions", "companyName,code,currentBalance,actions,aprobacion,estado")]
    // Unknown, hidden (Visible(false)) and repeated fields are ignored.
    [InlineData("cols=estado,nope,secret,estado,code", "estado,code,companyName,currentBalance,aprobacion,actions")]
    // Nothing usable listed: the default order.
    [InlineData("cols=nope,secret", "code,companyName,currentBalance,aprobacion,estado,actions")]
    // A column hidden with the column chooser keeps its place among the others.
    [InlineData("cols=estado,companyName&hide=companyName", "estado,companyName,code,currentBalance,aprobacion,actions")]
    public async Task Header_And_Cells_Share_One_Order(string query, string expected)
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();
        var hidden = Regex.Match(query, "hide=([^&]+)").Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var expectedCells = expected.Split(',').Except(hidden).ToArray();

        var shell = await client.GetStringAsync($"/netgrid/providers?{query}");
        Assert.Equal(expected.Split(','), HeaderFields(shell));
        Assert.Equal(expectedCells, FirstRowFields(shell));

        // The rows endpoint (what the client swaps in) agrees with the shell.
        var rows = await client.GetStringAsync($"/netgrid/providers/rows?{query}");
        Assert.Equal(expectedCells, FirstRowFields(rows));
    }

    [Fact]
    public async Task Without_Cols_The_Default_Order_Applies()
    {
        using var host = await BuildHostAsync();
        var shell = await host.GetTestClient().GetStringAsync("/netgrid/providers");

        Assert.Equal(DefaultOrder, HeaderFields(shell));
        Assert.Equal(DefaultOrder, FirstRowFields(shell));
    }

    // ---- Client contract (netopengrid.js) --------------------------------------------------------

    [Fact]
    public async Task Client_Normalizes_The_Order_With_The_Server_Rule()
    {
        using var host = await BuildHostAsync();
        var js = await host.GetTestClient().GetStringAsync("/_netgrid/netopengrid.js");

        var normalize = Method(js, "normalizeColumnOrder(order)");
        Assert.Contains("const known = this.defaultColumnOrder;", normalize);
        Assert.Contains("[...new Set(order.map(String))].filter((f) => known.includes(f))", normalize);
        Assert.Contains("if (listed.length === 0) return null;", normalize);
        Assert.Contains("return [...listed, ...known.filter((f) => !listed.includes(f))];", normalize);
    }

    [Fact]
    public async Task Client_Normalizes_Every_Source_Of_A_Column_Order()
    {
        using var host = await BuildHostAsync();
        var js = await host.GetTestClient().GetStringAsync("/_netgrid/netopengrid.js");

        // Saved order: normalized on load and written back when it changed, so stale state heals.
        var load = Method(js, "loadColumnOrder()");
        Assert.Contains("this.columnOrder = this.normalizeColumnOrder(raw ? JSON.parse(raw) : null);", load);
        Assert.Contains("if (raw !== null && raw !== JSON.stringify(this.columnOrder)) this.saveColumnOrder();", load);

        // The default order must be known before the saved one is read.
        Assert.True(
            js.IndexOf("this.defaultColumnOrder = (window.__NETGRID__", StringComparison.Ordinal) <
            js.IndexOf("this.loadColumnOrder();", StringComparison.Ordinal));

        // Drop, cols= (URL and saved views go through seedFromParams) and the pin-aware order.
        Assert.Contains("this.columnOrder = this.normalizeColumnOrder(order);", Method(js, "async onColumnDrop(field, event)"));
        Assert.Contains("this.columnOrder = this.normalizeColumnOrder(fields);", Method(js, "seedFromParams(params)"));
        Assert.Contains(
            "const base = this.normalizeColumnOrder(this.columnOrder) ?? this.defaultColumnOrder;",
            Method(js, "effectiveColumnOrder()"));

        // One place writes the order, always the normalized one.
        Assert.Single(Regex.Matches(js, "localStorage\\.setItem\\(`netgrid:cols:"));
        Assert.Contains("JSON.stringify(this.columnOrder)", Method(js, "saveColumnOrder()"));
        Assert.Contains("this.saveColumnOrder();", Method(js, "async applyView(query)"));
    }

    [Fact]
    public async Task Header_Reorder_Leaves_Leading_And_Library_Action_Cells_In_Place()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();
        var shell = await client.GetStringAsync("/netgrid/providers");
        var apply = Method(await client.GetStringAsync("/_netgrid/netopengrid.js"), "applyHeaderOrder()");

        // The header reorder only moves th[data-field] to the end (then the library's own actions
        // th): the expander th has no data-field, so it stays first, and a host field called
        // "actions" is an ordinary th[data-field].
        var header = Regex.Match(shell, "<thead.*?</thead>", RegexOptions.Singleline).Value;
        var cells = Regex.Matches(header, "<th[^>]*>").Select(m => m.Value).ToArray();
        Assert.DoesNotContain("data-field=", cells[0]);
        Assert.Contains("data-field=\"actions\"", cells[^1]);
        Assert.DoesNotContain(cells, th => th.Contains(" data-actions", StringComparison.Ordinal));

        Assert.Contains("th[data-field=\"${CSS.escape(field)}\"]", apply);
        Assert.Contains("th[data-actions]", apply);
    }

    private static string[] HeaderFields(string html) =>
        Regex.Matches(html, "<th[^>]*data-field=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();

    private static string[] FirstRowFields(string html)
    {
        // The shell wraps the rows in a <tbody>; the rows endpoint returns them bare.
        var tbody = html.IndexOf("<tbody", StringComparison.Ordinal);
        var body = tbody < 0 ? html : html[tbody..];
        var row = Regex.Matches(body, "<tr.*?</tr>", RegexOptions.Singleline)
            .Select(m => m.Value)
            .First(r => r.Contains("<td data-field=", StringComparison.Ordinal));
        return Regex.Matches(row, "<td data-field=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();
    }

    // The source of one component method: from its signature to the next method at the same indent.
    private static string Method(string js, string signature)
    {
        var start = js.IndexOf($"\n      {signature} {{", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{signature} not found in netopengrid.js");
        var end = js.IndexOf("\n      },", start, StringComparison.Ordinal);
        return js[start..end];
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
                        .AddGrid<Provider>("providers", options => options
                            .WithRowKey(p => p.Code)
                            .WithRowDetail(p => $"/providers/{p.Code}/detail")
                            .AddColumn("code", p => p.Code, c => c.Pinned())
                            .AddColumn("companyName", p => p.CompanyName)
                            .AddColumn("secret", p => p.Secret, c => c.Visible(false))
                            .AddColumn("currentBalance", p => p.CurrentBalance)
                            .AddColumn("aprobacion", p => p.Aprobacion)
                            .AddColumn("estado", p => p.Estado)
                            .AddColumn("actions", p => p.Code, c => c.PinnedRight()),
                            (_, opts) => new InMemoryGridDataSource<Provider>(opts, [new("P1", "Acme", "s", 10m, "ok", "activo")]));
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
