namespace NetOpenGrid.Domain.Columns;

/// <summary>Aggregate functions a numeric column can show. Combine with <c>|</c>.</summary>
[Flags]
public enum GridAggregate
{
    None = 0,
    Sum = 1,
    Avg = 2,
    Min = 4,
    Max = 8
}

/// <summary>Where aggregate rows are rendered. Combine with <c>|</c>.</summary>
[Flags]
public enum GridAggregateRows
{
    None = 0,

    /// <summary>Grand totals right below the column headers.</summary>
    Header = 1,

    /// <summary>Grand totals after the last row.</summary>
    Footer = 2,

    /// <summary>Subtotals inside each group's header row (visible while collapsed).</summary>
    GroupHeader = 4,

    /// <summary>Subtotals after an expanded group's rows.</summary>
    GroupFooter = 8,

    All = Header | Footer | GroupHeader | GroupFooter
}

public static class GridAggregateExtensions
{
    /// <summary>Individual functions in render order (Sum, Avg, Min, Max).</summary>
    public static IEnumerable<GridAggregate> Functions(this GridAggregate aggregates)
    {
        if ((aggregates & GridAggregate.Sum) != 0) yield return GridAggregate.Sum;
        if ((aggregates & GridAggregate.Avg) != 0) yield return GridAggregate.Avg;
        if ((aggregates & GridAggregate.Min) != 0) yield return GridAggregate.Min;
        if ((aggregates & GridAggregate.Max) != 0) yield return GridAggregate.Max;
    }
}
