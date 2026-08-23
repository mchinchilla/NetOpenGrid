namespace NetOpenGrid.Domain.GridQuerying;

public sealed record SortDescriptor(string Field, SortDirection Direction = SortDirection.Ascending);
