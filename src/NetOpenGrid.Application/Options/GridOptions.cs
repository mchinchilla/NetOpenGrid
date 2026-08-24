using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Domain.GridQuerying;

namespace NetOpenGrid.Application.Options;

public sealed record GridNavLink(string Label, string Href);

public sealed class GridOptions<T>
{
    public required string Id { get; init; }
    public required IReadOnlyList<GridColumn<T>> Columns { get; init; }
    public string Title { get; init; } = "Data grid";
    public string Subtitle { get; init; } = string.Empty;
    public int DefaultPageSize { get; init; } = PageRequest.DefaultPageSize;
    public int MaxPageSize { get; init; } = 500;
    public IReadOnlyList<int> PageSizeChoices { get; init; } = [10, 25, 50, 100];
    public int DebounceMilliseconds { get; init; } = 300;
    public string Theme { get; init; } = "grid";

    /// <summary>Minimum table-card height (CSS value). Keeps the grid from collapsing
    /// when few rows match; default ≈ 25 rows. Empty string disables it.</summary>
    public string MinHeight { get; init; } = "64rem";

    /// <summary>Max distinct values returned by Excel-style value-count lists.</summary>
    public int FilterValuesLimit { get; init; } = 200;

    public string EmptyMessage { get; init; } = "No records found.";
    public bool EnableRowSelection { get; init; }
    public Func<T, string?>? RowKey { get; init; }
    public IReadOnlyList<GridNavLink> NavLinks { get; init; } = [];

    private Dictionary<string, GridColumn<T>>? _columnIndex;

    public bool TryGetColumn(string? field, out GridColumn<T> column)
    {
        if (field is null)
        {
            column = null!;
            return false;
        }

        _columnIndex ??= Columns.ToDictionary(static c => c.Field, static c => c, StringComparer.Ordinal);
        return _columnIndex.TryGetValue(field, out column!);
    }
}
