using System.Globalization;
using NetOpenGrid.Domain.Abstractions;
using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Domain.GridQuerying;

namespace NetOpenGrid.Application.Strategies;

/// <summary>
/// Filter factory for parseable scalar keys (numbers, dates, guids).
/// Parsing is culture-invariant and done once per request value; comparisons are
/// precompiled delegate calls with zero reflection.
/// </summary>
internal sealed class ParsableFilterStrategy<TSource, TKey>(
    Func<TSource, TKey> selector,
    GridValueParser<TKey> parser) : IFilterStrategyFactory<TSource>
{
    private static readonly Comparer<TKey> KeyComparer = Comparer<TKey>.Default;

    public static ParsableFilterStrategy<TSource, TKey>? TryCreateDefault(
        Func<TSource, TKey> selector,
        GridValueParser<TKey>? customParser)
    {
        var parser = customParser ?? DefaultValueParsers<TKey>.TryGet();
        return parser is null ? null : new ParsableFilterStrategy<TSource, TKey>(selector, parser);
    }

    public bool SupportsEmptyValues => !typeof(TKey).IsValueType;

    public FilterOpSet SupportedOps
    {
        get
        {
            var ops = FilterOpSet.Numeric;
            if (SupportsEmptyValues)
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
                return SupportsEmptyValues ? new Lambda(item => selector(item) is null) : null;
            case FilterOperator.IsNotEmpty:
                return SupportsEmptyValues ? new Lambda(item => selector(item) is not null) : null;
        }

        if (!parser(rawValue ?? string.Empty, out var target))
        {
            return NeverMatch<TSource>.Instance;
        }

        return op switch
        {
            FilterOperator.Equals => new Lambda(item => CompareKey(item, target) == 0),
            FilterOperator.NotEquals => new Lambda(item => CompareKey(item, target) != 0),
            FilterOperator.GreaterThan => new Lambda(item => CompareKey(item, target) > 0),
            FilterOperator.GreaterThanOrEqual => new Lambda(item => CompareKey(item, target) >= 0),
            FilterOperator.LessThan => new Lambda(item => CompareKey(item, target) < 0),
            FilterOperator.LessThanOrEqual => new Lambda(item => CompareKey(item, target) <= 0),
            _ => null
        };
    }

    /// <summary>Null keys sort below every value.</summary>
    private int CompareKey(TSource item, TKey target)
    {
        var key = selector(item);
        return key is null ? -1 : KeyComparer.Compare(key, target);
    }

    private sealed class Lambda(Func<TSource, bool> matches) : IFilterStrategy<TSource>
    {
        public bool Matches(TSource item) => matches(item);
    }
}

internal sealed class NeverMatch<T> : IFilterStrategy<T>
{
    public static readonly NeverMatch<T> Instance = new();
    public bool Matches(T item) => false;
}

/// <summary>Hard-coded closed-type parser table. No MakeGenericType, no Activator, no reflection.</summary>
internal static class DefaultValueParsers<TKey>
{
    private static readonly GridValueParser<int> Int32 =
        static (string raw, out int v) => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

    private static readonly GridValueParser<long> Int64 =
        static (string raw, out long v) => long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

    private static readonly GridValueParser<short> Int16 =
        static (string raw, out short v) => short.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

    private static readonly GridValueParser<byte> UInt8 =
        static (string raw, out byte v) => byte.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

    private static readonly GridValueParser<sbyte> Int8 =
        static (string raw, out sbyte v) => sbyte.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

    private static readonly GridValueParser<uint> UInt32 =
        static (string raw, out uint v) => uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

    private static readonly GridValueParser<ulong> UInt64 =
        static (string raw, out ulong v) => ulong.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

    private static readonly GridValueParser<ushort> UInt16 =
        static (string raw, out ushort v) => ushort.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

    private static readonly GridValueParser<float> Single =
        static (string raw, out float v) => float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    private static readonly GridValueParser<double> Double =
        static (string raw, out double v) => double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    private static readonly GridValueParser<decimal> Decimal =
        static (string raw, out decimal v) => decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out v);

    private static readonly GridValueParser<DateTime> DateTimeValue =
        static (string raw, out DateTime v) =>
            System.DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out v);

    private static readonly GridValueParser<DateOnly> DateOnlyValue =
        static (string raw, out DateOnly v) => DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out v);

    private static readonly GridValueParser<DateTimeOffset> DateTimeOffsetValue =
        static (string raw, out DateTimeOffset v) => DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out v);

    private static readonly GridValueParser<Guid> GuidValue =
        static (string raw, out Guid v) => Guid.TryParse(raw, out v);

    /// <summary>
    /// Enum parser via <see cref="Enum.Parse(Type, string, bool)"/> on the closed enum type.
    /// No MakeGenericType, no Activator.
    /// </summary>
    public static GridValueParser<TKey>? TryGetEnum()
    {
        var enumType = typeof(TKey);
        if (!enumType.IsEnum)
        {
            return null;
        }

        return (string raw, out TKey parsed) =>
        {
            try
            {
                parsed = (TKey)Enum.Parse(enumType, raw.Trim(), ignoreCase: true);
                return true;
            }
            catch (ArgumentException)
            {
                parsed = default!;
                return false;
            }
        };
    }

    public static GridValueParser<TKey>? TryGet()
    {
        if (typeof(TKey) == typeof(int)) return (GridValueParser<TKey>)(Delegate)Int32;
        if (typeof(TKey) == typeof(long)) return (GridValueParser<TKey>)(Delegate)Int64;
        if (typeof(TKey) == typeof(short)) return (GridValueParser<TKey>)(Delegate)Int16;
        if (typeof(TKey) == typeof(byte)) return (GridValueParser<TKey>)(Delegate)UInt8;
        if (typeof(TKey) == typeof(sbyte)) return (GridValueParser<TKey>)(Delegate)Int8;
        if (typeof(TKey) == typeof(uint)) return (GridValueParser<TKey>)(Delegate)UInt32;
        if (typeof(TKey) == typeof(ulong)) return (GridValueParser<TKey>)(Delegate)UInt64;
        if (typeof(TKey) == typeof(ushort)) return (GridValueParser<TKey>)(Delegate)UInt16;
        if (typeof(TKey) == typeof(float)) return (GridValueParser<TKey>)(Delegate)Single;
        if (typeof(TKey) == typeof(double)) return (GridValueParser<TKey>)(Delegate)Double;
        if (typeof(TKey) == typeof(decimal)) return (GridValueParser<TKey>)(Delegate)Decimal;
        if (typeof(TKey) == typeof(DateTime)) return (GridValueParser<TKey>)(Delegate)DateTimeValue;
        if (typeof(TKey) == typeof(DateOnly)) return (GridValueParser<TKey>)(Delegate)DateOnlyValue;
        if (typeof(TKey) == typeof(DateTimeOffset)) return (GridValueParser<TKey>)(Delegate)DateTimeOffsetValue;
        if (typeof(TKey) == typeof(Guid)) return (GridValueParser<TKey>)(Delegate)GuidValue;
        return null;
    }
}
