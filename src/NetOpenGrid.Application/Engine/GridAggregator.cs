using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Domain.Results;

namespace NetOpenGrid.Application.Engine;

/// <summary>
/// One pass over the rows per call: Sum/Min/Max/count per aggregated column, Avg = Sum / count
/// of non-null values (same semantics as SQL's AVG). Columns whose values are all null get nulls.
/// </summary>
public static class GridAggregator
{
    public static GridAggregates Compute<T>(IEnumerable<T> rows, IReadOnlyList<GridColumn<T>> columns)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(columns);

        if (columns.Count == 0)
        {
            return GridAggregates.Empty;
        }

        var accumulators = new Accumulator[columns.Count];

        foreach (var row in rows)
        {
            for (var i = 0; i < columns.Count; i++)
            {
                if (columns[i].AggregateValue?.Invoke(row) is { } value)
                {
                    accumulators[i].Add(value);
                }
            }
        }

        var byField = new Dictionary<string, GridAggregateValues>(columns.Count, StringComparer.Ordinal);
        for (var i = 0; i < columns.Count; i++)
        {
            byField[columns[i].Field] = accumulators[i].ToValues(columns[i].Aggregates);
        }

        return new GridAggregates(byField);
    }

    private struct Accumulator
    {
        private decimal _sum;
        private decimal _min;
        private decimal _max;
        private long _count;

        public void Add(decimal value)
        {
            if (_count == 0)
            {
                _min = value;
                _max = value;
            }
            else
            {
                if (value < _min) _min = value;
                if (value > _max) _max = value;
            }

            _sum += value;
            _count++;
        }

        public readonly GridAggregateValues ToValues(GridAggregate requested)
        {
            if (_count == 0)
            {
                return new GridAggregateValues(null, null, null, null);
            }

            return new GridAggregateValues(
                (requested & GridAggregate.Sum) != 0 ? _sum : null,
                (requested & GridAggregate.Avg) != 0 ? _sum / _count : null,
                (requested & GridAggregate.Min) != 0 ? _min : null,
                (requested & GridAggregate.Max) != 0 ? _max : null);
        }
    }
}
