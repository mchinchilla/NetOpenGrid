using NetOpenGrid.Domain.Abstractions;
using NetOpenGrid.Domain.GridQuerying;

namespace NetOpenGrid.Application.Strategies;

internal sealed class EqualityFilterStrategy<TSource, TKey>(Func<TSource, TKey> selector)
    : IFilterStrategyFactory<TSource>
{
    private readonly bool _supportsEmpty = !typeof(TKey).IsValueType;

    public FilterOpSet SupportedOps
    {
        get
        {
            var ops = FilterOpSet.Equals | FilterOpSet.NotEquals;
            if (_supportsEmpty)
            {
                ops |= FilterOpSet.IsEmpty | FilterOpSet.IsNotEmpty;
            }

            return ops;
        }
    }

    public IFilterStrategy<TSource>? Create(FilterOperator op, string? rawValue)
    {
        if ((SupportedOps & FilterOperatorMapper.ToSet(op)) == 0)
        {
            return null;
        }

        switch (op)
        {
            case FilterOperator.IsEmpty:
                return new Lambda(item => selector(item) is null);
            case FilterOperator.IsNotEmpty:
                return new Lambda(item => selector(item) is not null);
        }

        var target = rawValue ?? string.Empty;

        return op switch
        {
            FilterOperator.Equals => new Lambda(item =>
                string.Equals(selector(item)?.ToString(), target, StringComparison.OrdinalIgnoreCase)),
            FilterOperator.NotEquals => new Lambda(item =>
                !string.Equals(selector(item)?.ToString(), target, StringComparison.OrdinalIgnoreCase)),
            _ => null
        };
    }

    private sealed class Lambda(Func<TSource, bool> matches) : IFilterStrategy<TSource>
    {
        public bool Matches(TSource item) => matches(item);
    }
}

internal sealed class EnumFilterStrategy<TSource, TKey>(Func<TSource, TKey> selector)
    : IFilterStrategyFactory<TSource>
    where TKey : struct, Enum
{
    public FilterOpSet SupportedOps => FilterOpSet.Equals | FilterOpSet.NotEquals;

    public IFilterStrategy<TSource>? Create(FilterOperator op, string? rawValue)
    {
        if ((SupportedOps & FilterOperatorMapper.ToSet(op)) == 0)
        {
            return null;
        }

        if (!Enum.TryParse(rawValue, ignoreCase: true, out TKey target))
        {
            return NeverMatch<TSource>.Instance;
        }

        return op switch
        {
            FilterOperator.Equals => new Lambda(item => EqualityComparer<TKey>.Default.Equals(selector(item), target)),
            FilterOperator.NotEquals => new Lambda(item => !EqualityComparer<TKey>.Default.Equals(selector(item), target)),
            _ => null
        };
    }

    private sealed class Lambda(Func<TSource, bool> matches) : IFilterStrategy<TSource>
    {
        public bool Matches(TSource item) => matches(item);
    }
}
