using System.Globalization;
using System.Text;
using NetOpenGrid.Application.DataSources;
using NetOpenGrid.Application.Json;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Domain.GridQuerying;
using NetOpenGrid.Example.Samples;
using NetOpenGrid.Infrastructure;
using NetOpenGrid.Infrastructure.Endpoints;

var builder = WebApplication.CreateBuilder(args);

var currenciesOptions = new JsonGridOptionsBuilder()
    .WithId("currencies")
    .WithTitle("Currencies")
    .WithSubtitle("JSON mode, same pipeline")
    .WithTheme("grid")
    .WithDefaultPageSize(6)
    .WithNavLinks(
        new GridNavLink("Products", "/netgrid/products"),
        new GridNavLink("Currencies", "/netgrid/currencies"))
    .AddColumn("code", c => c.Header("Code"))
    .AddColumn("name", c => c.Header("Name").Searchable())
    .AddColumn("rate", c => c
        .Header("USD rate")
        .Align(ColumnAlign.End)
        .AllowedOps(FilterOpSet.Numeric))
    .Build();

builder.Services.AddNetOpenGrid()
    .AddGrid<Product>("products", options => options
        .WithTitle("Product catalog")
        .WithSubtitle("Typed source with computed columns")
        .WithTheme("grid")
        .WithDefaultPageSize(10)
        .WithPageSizeChoices([10, 25, 50])
        .WithDebounce(250)
        .EnableRowSelection(p => p.Sku)
        .WithNavLinks(
            new GridNavLink("Products", "/netgrid/products"),
            new GridNavLink("Currencies", "/netgrid/currencies"))
        .AddColumn("sku", p => p.Sku, c => c.Header("SKU").Pinned())
        .AddColumn("name", p => p.Name, c => c.Searchable())
        .AddColumn("category", p => p.Category, c => c.Header("Category"))
        .AddColumn("price", p => p.Price, c => c
            .Header("Price")
            .Align(ColumnAlign.End)
            .Format(v => v.ToString("C2", CultureInfo.GetCultureInfo("en-US"))))
        .AddColumn("stock", p => p.Stock, c => c.Header("Stock").Align(ColumnAlign.End))
        .AddColumn("inventoryValue", p => p.Price * p.Stock, c => c
            .Header("Inventory value")
            .Align(ColumnAlign.End)
            .Format(v => v.ToString("C0", CultureInfo.GetCultureInfo("en-US"))))
        .AddColumn("releasedOn", p => p.ReleasedOn, c => c
            .Header("Released on")
            .Format(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
        .AddColumn("available", p => p.Available, c => c.Header("Status").RawCellHtml(p =>
            $"<span class=\"badge {(p.Available ? "badge-success" : "badge-muted")}\">{(p.Available ? "In stock" : "Sold out")}</span>")),
        (_, opts) => new InMemoryGridDataSource<Product>(opts, ProductData.All))
    .AddGrid(currenciesOptions, (_, opts) => new JsonGridDataSource(opts, CurrenciesPayload.Json));

var app = builder.Build();

app.MapNetOpenGrid();

app.MapGet("/", () => Results.Redirect("/netgrid/products"));

app.MapPost("/netgrid/{gridId}/export", async (string gridId, HttpRequest request, CancellationToken cancellationToken) =>
{
    await Task.Yield();

    if (gridId != "products")
    {
        return Results.BadRequest("Export is only wired for the products grid.");
    }

    var skus = request.Form["ids"].ToString()
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    if (skus.Length == 0)
    {
        return Results.BadRequest("No rows selected.");
    }

    var selected = skus.ToHashSet(StringComparer.Ordinal);
    var rows = ProductData.All.Where(p => selected.Contains(p.Sku));
    return Results.File(Encoding.UTF8.GetBytes(ProductData.ToCsv(rows)), "text/csv", "products.csv");
});

app.Run();

public partial class Program;
