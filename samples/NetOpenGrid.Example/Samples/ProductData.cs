using System.Globalization;
using System.Text;

namespace NetOpenGrid.Example.Samples;

public static class ProductData
{
    public static IReadOnlyList<Product> Create(int count = 140)
    {
        string[] adjectives =
        [
            "Turbo", "Ultra", "Compact", "Wireless", "Smart", "Classic",
            "Premium", "Eco", "Mini", "Pro", "Flex", "Nova"
        ];

        string[] nouns =
        [
            "Headphones", "Keyboard", "Lamp", "Bottle", "Backpack", "Speaker",
            "Charger", "Mouse", "Notebook", "Thermos", "Webcam", "Stand"
        ];

        Category[] categories = (Category[])Enum.GetValues<Category>();
        var random = new Random(7);
        var products = new List<Product>(count);

        for (var i = 0; i < count; i++)
        {
            var name = $"{adjectives[i % adjectives.Length]} {nouns[(i / adjectives.Length + i) % nouns.Length]}";
            var stock = random.Next(0, 250);

            products.Add(new Product(
                Sku: $"SKU-{i:D4}",
                Name: $"{name} {100 + i}",
                Category: categories[i * 3 % categories.Length],
                Price: Math.Round((decimal)(random.NextDouble() * 895 + 5) + 0.99m, 2),
                Stock: stock,
                ReleasedOn: DateOnly.FromDateTime(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)).AddDays(random.Next(2_400)),
                Available: stock > 0 && random.NextDouble() > 0.08));
        }

        return products;
    }

    public static readonly IReadOnlyList<Product> All = Create();

    public static string ToCsv(IEnumerable<Product> products)
    {
        var csv = new StringBuilder("Sku,Name,Category,Price,Stock,Released on,Available\n");
        foreach (var p in products)
        {
            csv.Append(p.Sku).Append(',')
               .Append(EscapeCsv(p.Name)).Append(',')
               .Append(p.Category).Append(',')
               .Append(p.Price.ToString(CultureInfo.InvariantCulture)).Append(',')
               .Append(p.Stock.ToString(CultureInfo.InvariantCulture)).Append(',')
               .Append(p.ReleasedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
               .Append(p.Available ? "true" : "false")
               .Append('\n');
        }

        return csv.ToString();
    }

    private static string EscapeCsv(string value) =>
        value.Contains(',') ? $"\"{value}\"" : value;
}
