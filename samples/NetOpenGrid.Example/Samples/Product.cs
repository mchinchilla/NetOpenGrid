namespace NetOpenGrid.Example.Samples;

public enum Category
{
    Electronics,
    Home,
    Sports,
    Toys,
    Books
}

public sealed record Product(
    string Sku,
    string Name,
    Category Category,
    decimal Price,
    int Stock,
    DateOnly ReleasedOn,
    bool Available);
