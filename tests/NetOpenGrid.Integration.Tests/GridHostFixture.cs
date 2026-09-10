using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using NetOpenGrid.Application.DataSources;
using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Infrastructure;

namespace NetOpenGrid.Integration.Tests;

/// <summary>
/// Plain DI container (no running web host) exposing a "products" grid and a "responsive"
/// grid, registered the same way <c>samples/NetOpenGrid.Example/Program.cs</c> registers
/// its products grid: <c>AddNetOpenGrid().AddGrid&lt;Product&gt;("products", ...)</c>
/// backed by an <see cref="InMemoryGridDataSource{T}"/>. Kept self-contained here
/// (rather than referencing the sample project) so tests that only need
/// <see cref="IServiceProvider"/> don't need a running host.
/// </summary>
public sealed class GridHostFixture
{
    public IServiceProvider Services { get; }

    public GridHostFixture()
    {
        var services = new ServiceCollection();

        services.AddNetOpenGrid()
            .AddGrid<Product>("products", options => options
                .WithTitle("Product catalog")
                .WithSubtitle("Typed source with computed columns")
                .WithTheme("grid")
                .WithDefaultPageSize(10)
                .WithPageSizeChoices([10, 25, 50])
                .WithDebounce(250)
                .EnableRowSelection(p => p.Sku)
                .AddColumn("sku", p => p.Sku, c => c.Header("SKU").Pinned())
                .AddColumn("name", p => p.Name, c => c.Searchable())
                .AddColumn("category", p => p.Category, c => c.Header("Category"))
                .AddColumn("price", p => p.Price, c => c
                    .Header("Price")
                    .Align(ColumnAlign.End)
                    .Format(v => v.ToString("C2", CultureInfo.GetCultureInfo("en-US"))))
                .AddColumn("stock", p => p.Stock, c => c.Header("Stock").Align(ColumnAlign.End))
                .AddColumn("releasedOn", p => p.ReleasedOn, c => c
                    .Header("Released on")
                    .Format(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
                .AddColumn("available", p => p.Available, c => c.Header("Status").RawCellHtml(p =>
                    $"<span class=\"badge {(p.Available ? "badge-success" : "badge-muted")}\">{(p.Available ? "In stock" : "Sold out")}</span>")),
                (_, opts) => new InMemoryGridDataSource<Product>(opts, ProductData.All))
            .AddGrid<ResponsiveItem>("responsive", options => options
                .WithTitle("Responsive columns")
                .AddColumn("code", r => r.Code, c => c.Header("Code"))
                .AddColumn("region", r => r.Region, c => c.Header("Region").HideBelow(ResponsiveBreakpoint.Lg)),
                (_, opts) => new InMemoryGridDataSource<ResponsiveItem>(opts, ResponsiveItemData.All));

        Services = services.BuildServiceProvider();
    }
}

file enum Category
{
    Electronics,
    Home,
    Sports,
    Toys,
    Books
}

file sealed record Product(
    string Sku,
    string Name,
    Category Category,
    decimal Price,
    int Stock,
    DateOnly ReleasedOn,
    bool Available);

file static class ProductData
{
    public static readonly IReadOnlyList<Product> All =
    [
        new("SKU-0001", "Turbo Headphones", Category.Electronics, 59.99m, 120, new DateOnly(2023, 3, 1), true),
        new("SKU-0002", "Ultra Keyboard", Category.Electronics, 89.99m, 45, new DateOnly(2022, 11, 15), true),
        new("SKU-0003", "Compact Lamp", Category.Home, 24.99m, 0, new DateOnly(2021, 6, 20), false),
        new("SKU-0004", "Wireless Bottle", Category.Sports, 14.99m, 200, new DateOnly(2023, 8, 9), true),
        new("SKU-0005", "Smart Backpack", Category.Toys, 39.99m, 12, new DateOnly(2020, 1, 30), true),
        new("SKU-0006", "Classic Notebook", Category.Books, 9.99m, 300, new DateOnly(2024, 2, 14), true),
    ];
}

/// <summary>Minimal shape for <see cref="ResponsiveColumnTests"/>: one plain column, one HideBelow(Lg) column.</summary>
file sealed record ResponsiveItem(string Code, string Region);

file static class ResponsiveItemData
{
    public static readonly IReadOnlyList<ResponsiveItem> All =
    [
        new("RG-001", "North"),
        new("RG-002", "South"),
    ];
}
