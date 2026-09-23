using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NetOpenGrid.Application.DataSources;
using NetOpenGrid.Application.Json;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Domain.GridQuerying;
using NetOpenGrid.Example.Samples;
using NetOpenGrid.Infrastructure;
using NetOpenGrid.Infrastructure.Endpoints;
using NetOpenGrid.Persistence;

var builder = WebApplication.CreateBuilder(args);

// The host app: plain Razor Pages. Each page embeds a grid through an <iframe src="/netgrid/{id}?embed=1">.
builder.Services.AddRazorPages();

// EF Core over an in-memory SQLite database that lives as long as the app (demo data only).
var sqlite = new SqliteConnection("DataSource=:memory:");
sqlite.Open();
builder.Services.AddDbContext<ShopDb>(o => o.UseSqlite(sqlite));

var usd = CultureInfo.GetCultureInfo("en-US");

// JSON mode: options built up front, rows are JsonElement, selectors are property names.
var currenciesOptions = new JsonGridOptionsBuilder()
    .WithId("currencies")
    .WithTitle("Currencies")
    .WithSubtitle("JSON mode, same pipeline")
    .WithTheme("midnight")
    .WithDefaultPageSize(10)
    .WithMinHeight("")                                   // the iframe sets the height
    .AddColumn("code", c => c.Header("Code"))
    .AddColumn("name", c => c.Header("Name").Searchable())
    .AddColumn("rate", c => c
        .Header("USD rate")
        .Align(ColumnAlign.End)
        .AllowedOps(FilterOpSet.Numeric))
    .Build();

builder.Services.AddNetOpenGrid(
        assets =>
        {
            assets.AssetPrefix = "/_netgrid";            // JS + HTMX + Alpine, served from the assembly
            assets.CssPath = "/css";                     // wwwroot/css/netopengrid-{theme}.css
        },
        // Every client-facing label. "en" (default) or "es"; switch it in appsettings.json.
        loc => loc.UseCulture(builder.Configuration["NetOpenGrid:Culture"] ?? "en"))

    // 1) Typed in-memory grid: computed column, custom formats, badges, a row-action column,
    //    pinned SKU and row selection.
    .AddGrid<Product>("products", options => options
        .WithTitle("Product catalog")
        .WithSubtitle("In-memory source, precompiled delegates")
        .WithTheme("grid")
        .WithDefaultPageSize(10)
        .WithPageSizeChoices([10, 25, 50])
        .WithDebounce(250)
        .WithMinHeight("")
        .EnableRowSelection(p => p.Sku)
        .AddColumn("sku", p => p.Sku, c => c.Header("SKU").Pinned())
        // Expression overloads derive the field and a humanized header: Name, Category, Stock, "Released on".
        .AddColumn(p => p.Name, c => c.Searchable())
        .AddColumn(p => p.Category)
        .AddColumn(p => p.Price, c => c
            .Align(ColumnAlign.End)
            .Format(v => v.ToString("C2", usd)))
        .AddColumn(p => p.Stock, c => c.Align(ColumnAlign.End))
        .AddColumn("inventoryValue", p => p.Price * p.Stock, c => c
            .Header("Inventory value")
            .Align(ColumnAlign.End)
            .Format(v => v.ToString("C0", usd)))
        .AddColumn(p => p.ReleasedOn, c => c
            .Format(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
        .AddColumn("available", p => p.Available, c => c.Header("Status").RawCellHtml(p =>
            $"<span class=\"badge {(p.Available ? "badge-success" : "badge-muted")}\">{(p.Available ? "In stock" : "Sold out")}</span>"))
        // Blank header + trusted HTML: a row-action column. target="_top" leaves the iframe.
        .AddColumn("details", p => p.Sku, c => c
            .Header("")
            .Sortable(false)
            .Filterable(false)
            .Searchable(false)
            .RawCellHtml(p =>
                $"<a class=\"font-medium text-brand-600\" target=\"_top\" href=\"/products/{WebUtility.UrlEncode(p.Sku)}\">Details</a>")),
        (_, opts) => new InMemoryGridDataSource<Product>(opts, ProductData.All))

    // 2) EF Core grid: filters, sorting, paging, value counts and grouping run in SQL.
    //    Push-down needs the Expression overloads (o => o.Prop).
    .AddGrid<Order>("orders", options => options
        .WithTitle("Orders")
        .WithSubtitle("EF Core + SQLite, pushed down to SQL")
        .WithTheme("grid")
        .WithDefaultPageSize(15)
        .WithPageSizeChoices([15, 30, 60])
        .WithMinHeight("")
        .AddColumn(o => o.Number, c => c.Header("Order").Pinned())
        .AddColumn(o => o.Customer, c => c.Searchable())
        .AddColumn(o => o.Country)
        .AddColumn(o => o.City)
        .AddColumn(o => o.Status, c => c.RawCellHtml(o =>
            $"<span class=\"badge badge-{WebUtility.HtmlEncode(o.Status)}\">{WebUtility.HtmlEncode(o.Status)}</span>"))
        .AddColumn(o => o.Items, c => c.Align(ColumnAlign.End))
        .AddColumn(o => o.Total, c => c
            .Align(ColumnAlign.End)
            .Format(v => v.ToString("C2", usd)))
        .AddColumn(o => o.PlacedOn, c => c
            .Format(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))),
        // sp is the root provider; the data source opens a scope per request, so the scoped DbContext is safe.
        (sp, opts) => new EFCoreGridDataSource<Order>(
            opts,
            sp,
            // OrderBy(Id) is the default order, so paging stays deterministic until the user sorts.
            scoped => scoped.GetRequiredService<ShopDb>().Orders.AsNoTracking().OrderBy(o => o.Id)))

    // 3) JSON grid.
    .AddGrid(currenciesOptions, (_, opts) => new JsonGridDataSource(opts, CurrenciesPayload.Json));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ShopDb>();
    db.Database.EnsureCreated();
    db.Orders.AddRange(OrderData.Create());
    db.SaveChanges();
}

app.UseStaticFiles();      // site.css + the compiled grid themes
app.MapNetOpenGrid();      // GET /netgrid/{id} · /rows · /values · /export · /_netgrid/*
app.MapRazorPages();       // the host pages under Pages/

// The toolbar's download button uses the built-in GET /netgrid/{id}/export (whole filtered dataset).
// The floating selection bar POSTs the selected row keys ("ids") here; this part is up to your app.
app.MapPost("/netgrid/{gridId}/export", (string gridId, HttpRequest request) =>
{
    if (gridId != "products")
    {
        return Results.BadRequest("Selection export is only wired for the products grid.");
    }

    var skus = request.Form["ids"].ToString()
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToHashSet(StringComparer.Ordinal);

    if (skus.Count == 0)
    {
        return Results.BadRequest("No rows selected.");
    }

    var rows = ProductData.All.Where(p => skus.Contains(p.Sku));
    return Results.File(Encoding.UTF8.GetBytes(ProductData.ToCsv(rows)), "text/csv", "products-selected.csv");
});

app.Run();

public partial class Program;
