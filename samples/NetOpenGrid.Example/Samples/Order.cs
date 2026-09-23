namespace NetOpenGrid.Example.Samples;

/// <summary>EF Core entity behind the "orders" grid.</summary>
public sealed class Order
{
    public int Id { get; set; }

    public string Number { get; set; } = string.Empty;

    public string Customer { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    /// <summary>pending · shipped · delivered · cancelled (matches the theme's badge-* classes).</summary>
    public string Status { get; set; } = string.Empty;

    public int Items { get; set; }

    public decimal Total { get; set; }

    public DateOnly PlacedOn { get; set; }
}
