using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NetOpenGrid.Application.Builders;
using NetOpenGrid.Application.Options;

namespace NetOpenGrid.Persistence.EFCore.Tests;

public sealed class EfProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public DateOnly ReleasedOn { get; set; }
    public bool Active { get; set; }
}

public sealed class TestDb(DbContextOptions<TestDb> options) : DbContext(options)
{
    public DbSet<EfProduct> Products => Set<EfProduct>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<EfProduct>().HasKey(p => p.Id);
}

public static class EfProductData
{
    public static IReadOnlyList<EfProduct> Create(int count = 30)
    {
        string[] categories = ["Electronics", "Home", "Sports", "Toys"];
        var products = new List<EfProduct>(count);

        for (var i = 0; i < count; i++)
        {
            products.Add(new EfProduct
            {
                Id = i + 1,
                Name = i % 5 == 0 ? $"Turbo Widget {i:D2}" : $"Product {i:D2}",
                Category = categories[i % categories.Length],
                Price = 5m + i * 7m + 0.5m,
                Stock = (i * 13) % 120,
                ReleasedOn = new DateOnly(2023, 1, 1).AddDays(i * 11),
                Active = i % 3 != 0
            });
        }

        return products;
    }

    public static readonly IReadOnlyList<EfProduct> All = Create();
}

public static class EfTestGrid
{
    public static GridOptions<EfProduct> Options(Action<GridOptionsBuilder<EfProduct>>? extra = null)
    {
        var builder = new GridOptionsBuilder<EfProduct>()
            .WithId("ef-products")
            .AddColumn(p => p.Id, c => c.Filterable(false))
            .AddColumn(p => p.Name, c => c.Searchable())
            .AddColumn(p => p.Category, c => c.Searchable())
            .AddColumn(p => p.Price)
            .AddColumn(p => p.Stock)
            .AddColumn(p => p.ReleasedOn, c => c.Header("Released on"))
            .AddColumn(p => p.Active);

        extra?.Invoke(builder);
        return builder.Build();
    }

    public static string ToCsv(IReadOnlyList<EfProduct> products)
    {
        var first = products[0];
        return $"{first.Id},{first.Name},{first.Category},{first.Price.ToString(CultureInfo.InvariantCulture)},{first.Stock},{first.ReleasedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)},{first.Active}";
    }
}
