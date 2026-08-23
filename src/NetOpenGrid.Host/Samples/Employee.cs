namespace NetOpenGrid.Host.Samples;

public enum Department
{
    Engineering,
    Design,
    Marketing,
    Sales,
    Support,
    Finance
}

public sealed record Employee(
    Guid Id,
    string FullName,
    string Email,
    Department Department,
    decimal Salary,
    DateOnly HiredOn,
    double PerformanceScore,
    bool IsActive);
