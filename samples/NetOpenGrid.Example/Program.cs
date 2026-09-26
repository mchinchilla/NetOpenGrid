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
// A named shared-cache database: every DbContext opens its own connection (a SqliteConnection is
// not thread-safe, and the grid does issue concurrent requests, e.g. virtual-scroll blocks), while
// this keep-alive connection stops the in-memory database from vanishing between requests.
const string shopDb = "Data Source=netopengrid-shop;Mode=Memory;Cache=Shared";
var keepAlive = new SqliteConnection(shopDb);
keepAlive.Open();
builder.Services.AddDbContext<ShopDb>(o => o.UseSqlite(shopDb));

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
            // CssPath is deliberately left unset: the compiled themes are embedded in the
            // assembly and served from AssetPrefix/css, so a host needs no stylesheet of its own
            // and there is no vendored copy to keep in sync. Set it (e.g. "/css") only when you
            // want to serve your own build from wwwroot instead.
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
        // Totals under the table, subtotals in each group header (visible while collapsed)
        // and after each expanded group. Computed over every filtered row, not just the page.
        .WithAggregateRows(GridAggregateRows.Footer | GridAggregateRows.GroupHeader | GridAggregateRows.GroupFooter)
        // Predefined views (menu next to the search box); users can save their own too.
        .AddView("In stock, cheapest first", "filter=available:equals:true&sort=price:asc")
        .AddView("Stock by category", "groupby=category&sort=stock:desc")
        .AddColumn("sku", p => p.Sku, c => c.Header("SKU").Pinned())
        // Expression overloads derive the field and a humanized header: Name, Category, Stock, "Released on".
        .AddColumn(p => p.Name, c => c.Searchable())
        .AddColumn(p => p.Category)
        .AddColumn(p => p.Price, c => c
            .Align(ColumnAlign.End)
            .Format(v => v.ToString("C2", usd))
            .ExcelFormat("\"$\"#,##0.00")
            .Aggregate(GridAggregate.Avg | GridAggregate.Min | GridAggregate.Max))
        .AddColumn(p => p.Stock, c => c
            .Align(ColumnAlign.End)
            .Aggregate(GridAggregate.Sum | GridAggregate.Avg))
        .AddColumn("inventoryValue", p => p.Price * p.Stock, c => c
            .Header("Inventory value")
            .Align(ColumnAlign.End)
            .Format(v => v.ToString("C0", usd))
            .ExcelFormat("\"$\"#,##0")
            .Aggregate(GridAggregate.Sum))
        .AddColumn(p => p.ReleasedOn, c => c
            .Format(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
        .AddColumn("available", p => p.Available, c => c.Header("Status").RawCellHtml(p =>
            $"<span class=\"badge {(p.Available ? "badge-success" : "badge-muted")}\">{(p.Available ? "In stock" : "Sold out")}</span>"))
        // Clicking a row opens the product; target "_top" leaves the iframe.
        .WithRowLink(p => $"/products/{WebUtility.UrlEncode(p.Sku)}", target: "_top")
        // Trailing actions column: a link, and an event the host page handles (see Pages/Index.cshtml).
        // Event actions use the row key, here the SKU from EnableRowSelection.
        .WithRowActions(a => a
            .Pinned()   // actions stay on the right edge while scrolling sideways
            .Link("Details", p => $"/products/{WebUtility.UrlEncode(p.Sku)}", target: "_top")
            .Event("archive", "Archive", RowActionStyle.Danger, visible: p => p.Available)),
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
        .WithAggregateRows(GridAggregateRows.Header | GridAggregateRows.GroupHeader)
        // 5,000 orders: no pager, rows load in blocks of 100 while scrolling (grouped views still page).
        .WithVirtualScroll(blockSize: 100, height: "70vh")
        .AddView("Delivered, biggest first", "filter=status:equals:delivered&sort=total:desc")
        .AddView("Pending by country", "filter=status:equals:pending&groupby=country")
        .AddColumn(o => o.Items, c => c
            .Align(ColumnAlign.End)
            .Aggregate(GridAggregate.Sum))
        .AddColumn(o => o.Total, c => c
            .Align(ColumnAlign.End)
            .Format(v => v.ToString("C2", usd))
            .ExcelFormat("\"$\"#,##0.00")
            .Aggregate(GridAggregate.Sum | GridAggregate.Avg))
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
    db.Orders.AddRange(OrderData.Create(5000));
    db.SaveChanges();
}

app.UseStaticFiles();      // site.css (the grid themes come from the assembly)
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
