namespace NetOpenGrid.Domain.Columns;

/// <summary>
/// What a column's filter value label resolver receives: the raw values of the filters active on
/// that column in the current request, all at once.
/// </summary>
public sealed class GridFilterValueLabelContext
{
    public GridFilterValueLabelContext(string field, IReadOnlyList<string> values, IServiceProvider services)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(services);
        Field = field;
        Values = values;
        Services = services;
    }

    /// <summary>The column's field.</summary>
    public string Field { get; }

    /// <summary>
    /// Distinct raw values of the active <c>equals</c>, <c>not-equals</c> and <c>in</c> filters on the
    /// column (for <c>in</c>, every element of the list), exactly as they came in the request.
    /// Never empty: the resolver is not called for a column without such a filter.
    /// </summary>
    public IReadOnlyList<string> Values { get; }

    /// <summary>
    /// The services of the current request (<c>HttpContext.RequestServices</c>), so the resolver can
    /// use the host's own scoped services (its DbContext, its connection). Outside a request (a
    /// runtime called with hand-built values) it is a provider that resolves nothing.
    /// </summary>
    public IServiceProvider Services { get; }
}

/// <summary>
/// Turns the raw values of a column's active filters into the text a person reads (an id into its
/// number or name). Returns raw value → label; a value left out keeps its raw text.
/// </summary>
public delegate Task<IReadOnlyDictionary<string, string>> GridFilterValueLabelResolver(
    GridFilterValueLabelContext context,
    CancellationToken cancellationToken);
