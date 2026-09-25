namespace NetOpenGrid.Persistence.Npgsql;

/// <summary>
/// A grid request that cannot be served as asked: a filter naming a field outside the map's
/// whitelist, a value that will not convert to its column's CLR type, or a grouping request
/// over more rows than <see cref="GridSqlMap.MaxGroupingRows"/> allows.
///
/// <para><b>This is a 400, not a 500.</b> Every case above is caused by the request, not by the
/// application — the grid's own query string is user-controlled, and a hand-edited
/// <c>?filter=</c> must not surface as an unhandled server error. Hosts should map this to
/// <c>400 Bad Request</c> in whatever exception middleware they already run; left unmapped it
/// behaves like any other unhandled exception, which is the wrong status and logs noise for
/// what is ordinary bad input.</para>
///
/// <para><see cref="Errors"/> is field-keyed so a host can surface it the same way it surfaces
/// model-validation failures, rather than flattening everything into one message.</para>
/// </summary>
public class GridQueryException : Exception
{
    /// <summary>Field name → the problems found with it. Never null; may be empty.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public GridQueryException(IReadOnlyDictionary<string, string[]> errors)
        : base("The grid request is not valid.")
    {
        Errors = errors ?? new Dictionary<string, string[]>();
    }

    public GridQueryException(string message, IReadOnlyDictionary<string, string[]> errors)
        : base(message)
    {
        Errors = errors ?? new Dictionary<string, string[]>();
    }

    public GridQueryException(string message)
        : this(message, new Dictionary<string, string[]>())
    {
    }
}
