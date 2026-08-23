namespace NetOpenGrid.Domain.GridQuerying;

public sealed record FilterDescriptor(string Field, FilterOperator Operator, string? Value);
