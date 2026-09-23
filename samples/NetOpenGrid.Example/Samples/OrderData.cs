namespace NetOpenGrid.Example.Samples;

public static class OrderData
{
    public static IReadOnlyList<Order> Create(int count = 320)
    {
        string[] customers =
        [
            "Acme Corp", "Globex", "Initech", "Umbrella", "Stark Industries", "Wayne Enterprises",
            "Hooli", "Soylent", "Wonka", "Tyrell", "Cyberdyne", "Aperture"
        ];

        (string Country, string City)[] places =
        [
            ("Honduras", "Tegucigalpa"), ("Honduras", "San Pedro Sula"),
            ("Mexico", "Monterrey"), ("Mexico", "Guadalajara"),
            ("Spain", "Madrid"), ("Spain", "Valencia"),
            ("United States", "Austin"), ("United States", "Seattle"),
            ("Colombia", "Bogota"), ("Peru", "Lima")
        ];

        string[] statuses = ["pending", "shipped", "delivered", "delivered", "delivered", "cancelled"];

        var random = new Random(42);
        var start = new DateOnly(2025, 1, 1);
        var orders = new List<Order>(count);

        for (var i = 0; i < count; i++)
        {
            var place = places[random.Next(places.Length)];
            var items = random.Next(1, 12);

            orders.Add(new Order
            {
                Id = i + 1,
                Number = $"SO-{10_000 + i}",
                Customer = customers[random.Next(customers.Length)],
                Country = place.Country,
                City = place.City,
                Status = statuses[random.Next(statuses.Length)],
                Items = items,
                Total = Math.Round(items * (decimal)(random.NextDouble() * 180 + 12), 2),
                PlacedOn = start.AddDays(random.Next(600))
            });
        }

        return orders;
    }
}
