namespace NetOpenGrid.Domain.GridQuerying;

[Flags]
public enum FilterOpSet : ushort
{
    None = 0,
    Equals = 1 << 0,
    NotEquals = 1 << 1,
    Contains = 1 << 2,
    StartsWith = 1 << 3,
    EndsWith = 1 << 4,
    GreaterThan = 1 << 5,
    GreaterThanOrEqual = 1 << 6,
    LessThan = 1 << 7,
    LessThanOrEqual = 1 << 8,
    IsEmpty = 1 << 9,
    IsNotEmpty = 1 << 10,
    In = 1 << 11,

    Text = Equals | NotEquals | Contains | StartsWith | EndsWith | IsEmpty | IsNotEmpty | In,
    Numeric = Equals | NotEquals | GreaterThan | GreaterThanOrEqual | LessThan | LessThanOrEqual | In,
    EqualityOnly = Equals | NotEquals | IsEmpty | IsNotEmpty | In,
    All = Text | Numeric
}

public static class FilterOperatorMapper
{
    private const string EqualsToken = "equals";
    private const string NotEqualsToken = "not-equals";
    private const string ContainsToken = "contains";
    private const string StartsWithToken = "starts-with";
    private const string EndsWithToken = "ends-with";
    private const string GreaterThanToken = "gt";
    private const string GreaterThanOrEqualToken = "gte";
    private const string LessThanToken = "lt";
    private const string LessThanOrEqualToken = "lte";
    private const string IsEmptyToken = "is-empty";
    private const string IsNotEmptyToken = "is-not-empty";
    private const string InToken = "in";

    public static bool TryParse(string? raw, out FilterOperator op)
    {
        switch (raw?.Trim().ToLowerInvariant())
        {
            case EqualsToken or "=": op = FilterOperator.Equals; return true;
            case NotEqualsToken or "!=": op = FilterOperator.NotEquals; return true;
            case ContainsToken or "~": op = FilterOperator.Contains; return true;
            case StartsWithToken: op = FilterOperator.StartsWith; return true;
            case EndsWithToken: op = FilterOperator.EndsWith; return true;
            case GreaterThanToken or ">": op = FilterOperator.GreaterThan; return true;
            case GreaterThanOrEqualToken or ">=": op = FilterOperator.GreaterThanOrEqual; return true;
            case LessThanToken or "<": op = FilterOperator.LessThan; return true;
            case LessThanOrEqualToken or "<=": op = FilterOperator.LessThanOrEqual; return true;
            case IsEmptyToken: op = FilterOperator.IsEmpty; return true;
            case IsNotEmptyToken: op = FilterOperator.IsNotEmpty; return true;
            case InToken: op = FilterOperator.In; return true;
            default: op = default; return false;
        }
    }

    /// <summary>Accepts compact forms like "&gt;=100" returning operator plus remaining value.</summary>
    public static bool TryParseLeadingSymbol(string? raw, out FilterOperator op, out string value)
    {
        foreach (var (symbol, parsed) in LeadingSymbols)
        {
            if (raw is not null && raw.StartsWith(symbol, StringComparison.Ordinal))
            {
                op = parsed;
                value = raw[symbol.Length..];
                return value.Length > 0;
            }
        }

        op = default;
        value = string.Empty;
        return false;
    }

    private static readonly (string Symbol, FilterOperator Op)[] LeadingSymbols =
    [
        (">=", FilterOperator.GreaterThanOrEqual),
        ("<=", FilterOperator.LessThanOrEqual),
        ("!=", FilterOperator.NotEquals),
        (">", FilterOperator.GreaterThan),
        ("<", FilterOperator.LessThan),
        ("=", FilterOperator.Equals),
        ("~", FilterOperator.Contains)
    ];

    public static string ToToken(FilterOperator op) => op switch
    {
        FilterOperator.Equals => EqualsToken,
        FilterOperator.NotEquals => NotEqualsToken,
        FilterOperator.Contains => ContainsToken,
        FilterOperator.StartsWith => StartsWithToken,
        FilterOperator.EndsWith => EndsWithToken,
        FilterOperator.GreaterThan => GreaterThanToken,
        FilterOperator.GreaterThanOrEqual => GreaterThanOrEqualToken,
        FilterOperator.LessThan => LessThanToken,
        FilterOperator.LessThanOrEqual => LessThanOrEqualToken,
        FilterOperator.IsEmpty => IsEmptyToken,
        FilterOperator.IsNotEmpty => IsNotEmptyToken,
        FilterOperator.In => InToken,
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, null)
    };

    public static FilterOpSet ToSet(FilterOperator op) => op switch
    {
        FilterOperator.Equals => FilterOpSet.Equals,
        FilterOperator.NotEquals => FilterOpSet.NotEquals,
        FilterOperator.Contains => FilterOpSet.Contains,
        FilterOperator.StartsWith => FilterOpSet.StartsWith,
        FilterOperator.EndsWith => FilterOpSet.EndsWith,
        FilterOperator.GreaterThan => FilterOpSet.GreaterThan,
        FilterOperator.GreaterThanOrEqual => FilterOpSet.GreaterThanOrEqual,
        FilterOperator.LessThan => FilterOpSet.LessThan,
        FilterOperator.LessThanOrEqual => FilterOpSet.LessThanOrEqual,
        FilterOperator.IsEmpty => FilterOpSet.IsEmpty,
        FilterOperator.IsNotEmpty => FilterOpSet.IsNotEmpty,
        FilterOperator.In => FilterOpSet.In,
        _ => FilterOpSet.None
    };

    public static IEnumerable<FilterOperator> ToOperators(FilterOpSet set)
    {
        foreach (FilterOperator op in Enum.GetValues<FilterOperator>())
        {
            if ((set & ToSet(op)) != 0)
            {
                yield return op;
            }
        }
    }
}
