using System.Text.Json;
using NetOpenGrid.Domain.Abstractions;
using NetOpenGrid.Domain.GridQuerying;

namespace NetOpenGrid.Application.Strategies;

internal sealed class StringFilterStrategy<TSource>(Func<TSource, string?> selector) : IFilterStrategyFactory<TSource>
{
    private readonly IFilterStrategy<TSource> _empty = new Lambda(item => string.IsNullOrWhiteSpace(selector(item)));
    private readonly IFilterStrategy<TSource> _notEmpty = new Lambda(item => !string.IsNullOrWhiteSpace(selector(item)));

    public FilterOpSet SupportedOps => FilterOpSet.Text | FilterOpSet.Numeric;

    public IFilterStrategy<TSource>? Create(FilterOperator op, string? rawValue)
    {
        if ((SupportedOps & FilterOperatorMapper.ToSet(op)) == 0)
        {
            return null;
        }

        var value = rawValue ?? string.Empty;

        return op switch
        {
            FilterOperator.Equals => new Lambda(item => string.Equals(selector(item), value, StringComparison.OrdinalIgnoreCase)),
            FilterOperator.NotEquals => new Lambda(item => !string.Equals(selector(item), value, StringComparison.OrdinalIgnoreCase)),
            FilterOperator.Contains => new Lambda(item => selector(item)?.Contains(value, StringComparison.OrdinalIgnoreCase) == true),
            FilterOperator.StartsWith => new Lambda(item => selector(item)?.StartsWith(value, StringComparison.OrdinalIgnoreCase) == true),
            FilterOperator.EndsWith => new Lambda(item => selector(item)?.EndsWith(value, StringComparison.OrdinalIgnoreCase) == true),
            FilterOperator.GreaterThan => Ordered(value, static c => c > 0),
            FilterOperator.GreaterThanOrEqual => Ordered(value, static c => c >= 0),
            FilterOperator.LessThan => Ordered(value, static c => c < 0),
            FilterOperator.LessThanOrEqual => Ordered(value, static c => c <= 0),
            FilterOperator.IsEmpty => _empty,
            FilterOperator.IsNotEmpty => _notEmpty,
            FilterOperator.In => In(value),
            _ => null
        };
    }

    private IFilterStrategy<TSource> In(string json)
    {
        var set = ParseInValues(json, static v => v, StringComparer.OrdinalIgnoreCase);
        return new Lambda(item => set.Contains(selector(item) ?? string.Empty));
    }

    internal static HashSet<string> ParseInValues(string json, Func<string, string> transform, IEqualityComparer<string>? comparer = null) =>
        new(ParseInList(json).Select(transform), comparer ?? StringComparer.Ordinal);

    private static List<string> ParseInList(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private IFilterStrategy<TSource> Ordered(string value, Func<int, bool> accept) =>
        new Lambda(item =>
        {
            var current = selector(item);
            return current is not null && accept(string.Compare(current, value, StringComparison.OrdinalIgnoreCase));
        });

    private sealed class Lambda(Func<TSource, bool> matches) : IFilterStrategy<TSource>
    {
        public bool Matches(TSource item) => matches(item);
    }
}
