using System.IO.Compression;
using System.Net;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetOpenGrid.Application.Builders;
using NetOpenGrid.Application.DataSources;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Infrastructure;
using NetOpenGrid.Infrastructure.Endpoints;
using NetOpenGrid.Infrastructure.Export;
using Xunit;

namespace NetOpenGrid.Integration.Tests;

public class XlsxExportTests
{
    private enum Tier { Basic, Gold }

    private sealed record Item(int Id, string Name, decimal Price, DateOnly Released, DateTime Updated, bool Active, Tier Tier);

    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly Item[] Items =
    [
        .. Enumerable.Range(1, 9).Select(i => new Item(i, $"Item {i}", 10.5m * i, new DateOnly(2024, 1, i), new DateTime(2024, 1, i, 13, 30, 0), i % 2 == 0, Tier.Basic)),
        new(10, "=HYPERLINK(\"http://evil\") <b>&\u0001", 99.99m, new DateOnly(2024, 2, 1), new DateTime(2024, 2, 1), true, Tier.Gold)
    ];

    [Fact]
    public async Task Xlsx_Is_A_Valid_Package_With_Typed_Cells()
    {
        using var host = await BuildHostAsync();
        var response = await host.GetTestClient().GetAsync("/netgrid/items/export?format=xlsx&sort=id");

        response.EnsureSuccessStatusCode();
        Assert.Equal(GridXlsxWriter.ContentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("items.xlsx", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));

        var (sheet, workbook, styles) = await OpenAsync(response);
        var rows = sheet.Descendants(S + "row").ToArray();
        Assert.Equal(11, rows.Length);                                                   // header + 10 rows over 4 pages of 3

        var header = rows[0].Elements(S + "c").ToArray();
        Assert.All(header, c => Assert.Equal("1", (string?)c.Attribute("s")));           // bold style
        Assert.Equal(["Id", "Name", "Price", "Released", "Updated", "Active", "Tier"], header.Select(Text));

        var first = Cells(rows[1]);
        Assert.Null(first["A2"].Attribute("t"));                                          // number
        Assert.Equal("1", first["A2"].Value);
        Assert.Equal("10.5", first["C2"].Element(S + "v")!.Value);
        Assert.Equal("4", (string?)first["C2"].Attribute("s"));                          // custom ExcelFormat
        Assert.Equal("45292", first["D2"].Element(S + "v")!.Value);                       // 2024-01-01 as a serial
        Assert.Equal("2", (string?)first["D2"].Attribute("s"));                          // date style
        Assert.Equal("3", (string?)first["E2"].Attribute("s"));                          // date-time style
        Assert.Equal("b", (string?)first["F2"].Attribute("t"));
        Assert.Equal("0", first["F2"].Value);
        Assert.Equal("inlineStr", (string?)first["G2"].Attribute("t"));                 // enum as its text
        Assert.Equal("Basic", Text(first["G2"]));

        var tricky = Cells(rows[10]);
        Assert.Equal("inlineStr", (string?)tricky["B11"].Attribute("t"));                 // never a formula
        Assert.Empty(sheet.Descendants(S + "f"));
        Assert.Equal("=HYPERLINK(\"http://evil\") <b>&", Text(tricky["B11"]));            // escaped, control char dropped

        Assert.Equal("A1:G11", (string?)sheet.Element(S + "autoFilter")!.Attribute("ref"));
        Assert.Equal("Items export", (string?)workbook.Descendants(S + "sheet").Single().Attribute("name"));
        Assert.Contains(styles.Descendants(S + "numFmt"), f => (string?)f.Attribute("formatCode") == "\"$\"#,##0.00");
    }

    [Fact]
    public async Task Xlsx_Respects_Hidden_Columns_And_Filters()
    {
        using var host = await BuildHostAsync();
        var response = await host.GetTestClient().GetAsync("/netgrid/items/export?format=xlsx&hide=price,updated&filter=active:equals:true");

        var (sheet, _, _) = await OpenAsync(response);
        var rows = sheet.Descendants(S + "row").ToArray();

        Assert.Equal(["Id", "Name", "Released", "Active", "Tier"], rows[0].Elements(S + "c").Select(Text));
        Assert.Equal(1 + Items.Count(i => i.Active), rows.Length);
    }

    [Fact]
    public async Task Empty_Result_Still_Yields_A_Workbook_With_Headers()
    {
        using var host = await BuildHostAsync();
        var response = await host.GetTestClient().GetAsync("/netgrid/items/export?format=xlsx&filter=name:equals:nobody");

        var (sheet, _, _) = await OpenAsync(response);
        Assert.Single(sheet.Descendants(S + "row"));
        Assert.Equal("A1:G1", (string?)sheet.Element(S + "autoFilter")!.Attribute("ref"));
    }

    [Fact]
    public async Task Disabled_Or_Unknown_Formats_Are_Rejected()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/netgrid/csvonly/export?format=xlsx")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/netgrid/items/export?format=pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/netgrid/csvonly/export")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/netgrid/noexport/export")).StatusCode);
    }

    [Fact]
    public async Task Xlsx_Honours_The_Row_Cap()
    {
        using var host = await BuildHostAsync();
        var response = await host.GetTestClient().GetAsync("/netgrid/capped/export?format=xlsx");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Toolbar_Offers_A_Menu_Only_When_Both_Formats_Are_On()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();

        var both = await client.GetStringAsync("/netgrid/items");
        Assert.Contains("exportAs('csv')", both);
        Assert.Contains("exportAs('xlsx')", both);
        Assert.Contains("aria-controls=\"items-export\"", both);

        var csvOnly = await client.GetStringAsync("/netgrid/csvonly");
        Assert.Contains("@click=\"exportAs('csv')\" class=\"btn-icon\"", csvOnly);
        Assert.DoesNotContain("exportAs('xlsx')", csvOnly);

        var none = await client.GetStringAsync("/netgrid/noexport");
        Assert.DoesNotContain("exportAs(", none);
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(701, "ZZ")]
    [InlineData(702, "AAA")]
    public void Column_Letters(int index, string expected) => Assert.Equal(expected, GridXlsxWriter.ColumnLetter(index));

    [Theory]
    [InlineData("Sales: Q1/Q2 [draft]?", "Sales Q1Q2 draft")]
    [InlineData("'quoted'", "quoted")]
    [InlineData("   ", "Sheet1")]
    [InlineData("A very long title that goes past thirty-one characters", "A very long title that goes pas")]
    public void Sheet_Names_Are_Made_Valid(string title, string expected) => Assert.Equal(expected, GridXlsxWriter.SheetName(title));

    private static async Task<(XElement Sheet, XElement Workbook, XElement Styles)> OpenAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        foreach (var part in new[] { "[Content_Types].xml", "_rels/.rels", "xl/_rels/workbook.xml.rels" })
        {
            Assert.NotNull(zip.GetEntry(part));
        }

        XElement Load(string name)
        {
            using var stream = zip.GetEntry(name)!.Open();
            return XElement.Load(stream);   // throws on malformed XML
        }

        return (Load("xl/worksheets/sheet1.xml"), Load("xl/workbook.xml"), Load("xl/styles.xml"));
    }

    private static Dictionary<string, XElement> Cells(XElement row) =>
        row.Elements(S + "c").ToDictionary(c => (string)c.Attribute("r")!);

    private static string Text(XElement cell) => cell.Element(S + "is")?.Element(S + "t")?.Value ?? cell.Value;

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
                        .AddGrid<Item>("csvonly", options => Configure(options).WithExportFormats(GridExportFormats.Csv),
                            (_, opts) => new InMemoryGridDataSource<Item>(opts, Items))
                        .AddGrid<Item>("capped", options => Configure(options).WithMaxExportRows(2),
                            (_, opts) => new InMemoryGridDataSource<Item>(opts, Items))
                        .AddGrid<Item>("noexport", options => Configure(options).WithExportFormats(GridExportFormats.None),
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
        .WithTitle("Items export")
        .WithMaxPageSize(3)
        .WithDefaultPageSize(3)
        .WithPageSizeChoices([3])
        .AddColumn("id", i => i.Id)
        .AddColumn("name", i => i.Name)
        .AddColumn("price", i => i.Price, c => c.ExcelFormat("\"$\"#,##0.00"))
        .AddColumn("released", i => i.Released)
        .AddColumn("updated", i => i.Updated)
        .AddColumn("active", i => i.Active)
        .AddColumn("tier", i => i.Tier);
}
