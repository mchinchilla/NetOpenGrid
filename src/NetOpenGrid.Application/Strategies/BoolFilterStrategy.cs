using System.Text.Json;
using NetOpenGrid.Domain.Abstractions;
using NetOpenGrid.Domain.GridQuerying;

namespace NetOpenGrid.Application.Strategies;

internal sealed class BoolFilterStrategy<TSource>(Func<TSource, bool> selector)
    : IFilterStrategyFactory<TSource>
{
    public FilterOpSet SupportedOps => FilterOpSet.Equals | FilterOpSet.NotEquals | FilterOpSet.In;

    public IFilterStrategy<TSource>? Create(FilterOperator op, string? rawValue)
    {
        if ((SupportedOps & FilterOperatorMapper.ToSet(op)) == 0)
        {
            return null;
        }

        if (op == FilterOperator.In)
        {
            List<string> values;
            try
            {
                values = JsonSerializer.Deserialize<List<string>>(rawValue ?? string.Empty) ?? [];
            }
            catch (JsonException)
            {
                values = [];
            }

            var targets = values
                .Where(v => bool.TryParse(v, out _))
                .Select(bool.Parse)
                .ToHashSet();

            return targets.Count == 0
                ? NeverMatch<TSource>.Instance
                : new Lambda(item => targets.Contains(selector(item)));
        }

        if (!bool.TryParse(rawValue, out var target))
        {
            return NeverMatch<TSource>.Instance;
        }

        return op switch
        {
            FilterOperator.Equals => new Lambda(item => selector(item) == target),
            FilterOperator.NotEquals => new Lambda(item => selector(item) != target),
            _ => null
        };
    }

    private sealed class Lambda(Func<TSource, bool> matches) : IFilterStrategy<TSource>
    {
        public bool Matches(TSource item) => matches(item);
    }
}
