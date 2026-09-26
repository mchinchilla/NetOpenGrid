using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetOpenGrid.Application.Builders;
using NetOpenGrid.Application.DataSources;
using NetOpenGrid.Infrastructure;
using NetOpenGrid.Infrastructure.Endpoints;
using NetOpenGrid.Infrastructure.Export;
using Xunit;

namespace NetOpenGrid.Integration.Tests;

public class ExportStreamingTests
{
    private sealed record Item(int Id, string Note);

    private static readonly Item[] Items =
    [
        .. Enumerable.Range(1, 9).Select(i => new Item(i, $"note {i}")),
        new(10, "=HYPERLINK(\"http://evil\",\"x\")")
    ];

    [Fact]
    public async Task Export_Streams_Every_Page_When_Larger_Than_MaxPageSize()
    {
        using var host = await BuildHostAsync();
        var csv = await host.GetTestClient().GetStringAsync("/netgrid/items/export?sort=id");

        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(Items.Length + 1, lines.Length);   // header + all rows, across 4 pages of 3
        Assert.Equal("Id,Note", lines[0]);
        Assert.Equal("1,note 1", lines[1]);
    }

    [Fact]
    public async Task Export_Starts_With_Utf8_Bom()
    {
        using var host = await BuildHostAsync();
        var bytes = await host.GetTestClient().GetByteArrayAsync("/netgrid/items/export");

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
    }

    [Fact]
    public async Task Export_Neutralizes_Formula_Cells()
    {
        using var host = await BuildHostAsync();
        var csv = await host.GetTestClient().GetStringAsync("/netgrid/items/export?sort=id");

        Assert.Contains("10,\"'=HYPERLINK(\"\"http://evil\"\",\"\"x\"\")\"", csv);
    }

    [Fact]
    public async Task Export_Above_MaxExportRows_Is_Rejected_Before_Streaming()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();

        var rejected = await client.GetAsync("/netgrid/capped/export");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        Assert.Contains("limit of 4", await rejected.Content.ReadAsStringAsync());

        // Narrowed by a filter it fits again.
        var allowed = await client.GetAsync("/netgrid/capped/export?filter=id:lte:4");
        allowed.EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+cmd", "'+cmd")]
    [InlineData("-cmd", "'-cmd")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tx", "'\tx")]
    [InlineData("-12.5", "-12.5")]
    [InlineData("+3", "+3")]
    [InlineData("plain", "plain")]
    [InlineData("", "")]
    public void Escape_Prefixes_Formula_Like_Values_But_Not_Numbers(string value, string expected)
    {
        Assert.Equal(expected, GridCsvExporter.Escape(value));
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
                        .AddGrid<Item>("items", options => Configure(options),
                            (_, opts) => new InMemoryGridDataSource<Item>(opts, Items))
                        .AddGrid<Item>("capped", options => Configure(options).WithMaxExportRows(4),
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

    private static GridOptionsBuilder<Item> Configure(GridOptionsBuilder<Item> options) => options
        .WithMaxPageSize(3)
        .WithDefaultPageSize(3)
        .WithPageSizeChoices([3])
        .AddColumn("id", i => i.Id)
        .AddColumn("note", i => i.Note);
}
