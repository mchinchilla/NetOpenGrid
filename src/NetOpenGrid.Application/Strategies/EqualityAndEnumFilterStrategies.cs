using System.Text.Json;
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
            var ops = FilterOpSet.Equals | FilterOpSet.NotEquals | FilterOpSet.In;
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
            case FilterOperator.In:
                return CreateIn(rawValue ?? string.Empty);
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

    private IFilterStrategy<TSource> CreateIn(string json)
    {
        List<string> values;
        try
        {
            values = JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            values = [];
        }

        var set = values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new Lambda(item => set.Contains(selector(item)?.ToString() ?? string.Empty));
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
    public FilterOpSet SupportedOps => FilterOpSet.Equals | FilterOpSet.NotEquals | FilterOpSet.In;

    public IFilterStrategy<TSource>? Create(FilterOperator op, string? rawValue)
    {
        if ((SupportedOps & FilterOperatorMapper.ToSet(op)) == 0)
        {
            return null;
        }

        if (op == FilterOperator.In)
        {
            return CreateIn(rawValue ?? string.Empty);
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

    private IFilterStrategy<TSource> CreateIn(string json)
    {
        List<string> values;
        try
        {
            values = JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            values = [];
        }

        var targets = new HashSet<TKey>();
        foreach (var value in values)
        {
            if (Enum.TryParse(value, ignoreCase: true, out TKey parsed))
            {
                targets.Add(parsed);
            }
        }

        return targets.Count == 0
            ? NeverMatch<TSource>.Instance
            : new Lambda(item => targets.Contains(selector(item)));
    }

    private sealed class Lambda(Func<TSource, bool> matches) : IFilterStrategy<TSource>
    {
        public bool Matches(TSource item) => matches(item);
    }
}
