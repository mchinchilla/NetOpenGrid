using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Domain.GridQuerying;

namespace NetOpenGrid.Domain.Abstractions;

public sealed record GridValueCount(string Value, int Count);

public sealed record GridValueCounts(IReadOnlyList<GridValueCount> Values, int TotalDistinct);

/// <summary>
/// Excel-style value counts: distinct values of a column with row counts, computed under the
/// current query context EXCLUDING the column's own filter (classic Excel semantics).
/// </summary>
public interface IGridValueCountSource<T>
{
    ValueTask<GridValueCounts> GetValuesAsync(GridColumn<T> column, GridQuery context, CancellationToken cancellationToken = default);
}
