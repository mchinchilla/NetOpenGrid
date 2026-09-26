using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Domain.GridQuerying;

namespace NetOpenGrid.Domain.Results;

/// <summary>Aggregate results for one column. A function that was not requested, or ran over no values, is null.</summary>
public sealed record GridAggregateValues(decimal? Sum, decimal? Avg, decimal? Min, decimal? Max)
{
    public decimal? Get(GridAggregate function) => function switch
    {
        GridAggregate.Sum => Sum,
        GridAggregate.Avg => Avg,
        GridAggregate.Min => Min,
        GridAggregate.Max => Max,
        _ => null
    };
}

/// <summary>Aggregate results by column field.</summary>
public sealed class GridAggregates
{
    public static readonly GridAggregates Empty = new(new Dictionary<string, GridAggregateValues>(StringComparer.Ordinal));

    private readonly IReadOnlyDictionary<string, GridAggregateValues> _byField;

    public GridAggregates(IReadOnlyDictionary<string, GridAggregateValues> byField)
    {
        ArgumentNullException.ThrowIfNull(byField);
        _byField = byField;
    }

    public bool IsEmpty => _byField.Count == 0;

    public IReadOnlyDictionary<string, GridAggregateValues> ByField => _byField;

    public GridAggregateValues? For(string field) => _byField.GetValueOrDefault(field);
}

/// <summary>Capability for sources able to compute grand-total aggregates over the filtered set (all pages).</summary>
public interface IGridAggregateSource<T>
{
    ValueTask<GridAggregates> GetAggregatesAsync(GridQuery query, CancellationToken cancellationToken = default);
}
