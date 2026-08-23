namespace NetOpenGrid.Application.Binding;

/// <summary>
/// Transport-agnostic multi-value request representation (query string / form / headers).
/// </summary>
public sealed class GridRequestValues
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _values;

    public static GridRequestValues Empty { get; } =
        new(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));

    public GridRequestValues(IEnumerable<KeyValuePair<string, IReadOnlyList<string>>> entries)
    {
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (key, values) in entries)
        {
            map[key] = values is IReadOnlyList<string> list ? list : [.. values];
        }

        _values = map;
    }

    public string? Get(string name) =>
        _values.TryGetValue(name, out var all) && all.Count > 0 ? all[0] : null;

    public IReadOnlyList<string> GetAll(string name) =>
        _values.TryGetValue(name, out var all) ? all : [];
}
