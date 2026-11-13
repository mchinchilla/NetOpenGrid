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

    private GridRequestValues(IReadOnlyDictionary<string, IReadOnlyList<string>> values, IServiceProvider? services)
    {
        _values = values;
        Services = services;
    }

    /// <summary>
    /// The services of the request these values came from (<c>HttpContext.RequestServices</c>);
    /// <c>HttpRequest.ToGridRequestValues()</c> fills it in. Column callbacks that need the host's
    /// scoped services, such as <c>FilterValueLabels</c>, get it from here. Null outside a request.
    /// </summary>
    public IServiceProvider? Services { get; }

    /// <summary>The same values, bound to the services of a request.</summary>
    public GridRequestValues WithServices(IServiceProvider? services) => new(_values, services);

    public string? Get(string name) =>
        _values.TryGetValue(name, out var all) && all.Count > 0 ? all[0] : null;

    public IReadOnlyList<string> GetAll(string name) =>
        _values.TryGetValue(name, out var all) ? all : [];
}
