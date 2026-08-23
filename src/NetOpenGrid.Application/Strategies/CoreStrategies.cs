using System.Globalization;
using NetOpenGrid.Domain.Abstractions;
using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Domain.GridQuerying;

namespace NetOpenGrid.Application.Strategies;

/// <summary>Cached per closed TKey. Formats values without boxing or reflection.</summary>
internal static class DefaultValueFormatter<TKey>
{
    public static readonly Func<TKey, string?> Format = Create();

    private static Func<TKey, string?> Create()
    {
        if (typeof(TKey) == typeof(string))
        {
            return static v => (string?)(object?)v;
        }

        if (typeof(TKey) == typeof(bool))
        {
            return static v => (bool)(object)v! ? "true" : "false";
        }

        if (typeof(TKey).IsValueType && typeof(IFormattable).IsAssignableFrom(typeof(TKey)))
        {
            return static v => v is null ? null : ((IFormattable)(object)v).ToString(null, CultureInfo.InvariantCulture);
        }

        return static v => v?.ToString();
    }
}

internal sealed class KeySortStrategy<TSource, TKey>(Func<TSource, TKey> keySelector, IComparer<TKey> comparer)
    : ISortStrategy<TSource>
{
    public int Compare(TSource x, TSource y)
    {
        var kx = keySelector(x);
        var ky = keySelector(y);

        if (kx is null)
        {
            return ky is null ? 0 : -1;
        }

        if (ky is null)
        {
            return 1;
        }

        return comparer.Compare(kx, ky);
    }
}

internal sealed class StringSearchStrategy<TSource>(Func<TSource, string?> selector) : ISearchStrategy<TSource>
{
    public bool Matches(TSource item, string term) =>
        selector(item)?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
}

internal sealed class CustomSearchStrategy<TSource>(Func<TSource, string?, bool> matcher) : ISearchStrategy<TSource>
{
    public bool Matches(TSource item, string term) => matcher(item, term);
}
