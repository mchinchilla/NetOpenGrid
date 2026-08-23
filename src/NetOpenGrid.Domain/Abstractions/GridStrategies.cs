using NetOpenGrid.Domain.GridQuerying;

namespace NetOpenGrid.Domain.Abstractions;

/// <summary>Precompiled comparison strategy for a column. No reflection at request time.</summary>
public interface ISortStrategy<T>
{
    int Compare(T x, T y);
}

/// <summary>Precompiled filter strategy for a single operator/value pair.</summary>
public interface IFilterStrategy<in T>
{
    bool Matches(T item);
}

/// <summary>Creates filter strategies per request from operator + raw value, without reflection.</summary>
public interface IFilterStrategyFactory<T>
{
    FilterOpSet SupportedOps { get; }

    IFilterStrategy<T>? Create(FilterOperator op, string? rawValue);
}

/// <summary>Precompiled global-search matcher for a column.</summary>
public interface ISearchStrategy<in T>
{
    bool Matches(T item, string term);
}
