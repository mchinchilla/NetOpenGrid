using System.Globalization;
using NetOpenGrid.Domain.Columns;

namespace NetOpenGrid.Application.Builders;

/// <summary>
/// Build-time glue between a typed numeric column and aggregation: a decimal selector
/// for in-memory accumulation, and a formatter that turns a decimal result back into
/// the column's own display. Everything is resolved once per column; no reflection per request.
/// </summary>
internal static class AggregateSupport<TSource, TKey>
{
    private static readonly Type Underlying = Nullable.GetUnderlyingType(typeof(TKey)) ?? typeof(TKey);

    public static bool IsNumeric =>
        Underlying == typeof(byte) || Underlying == typeof(sbyte) || Underlying == typeof(short) ||
        Underlying == typeof(ushort) || Underlying == typeof(int) || Underlying == typeof(uint) ||
        Underlying == typeof(long) || Underlying == typeof(ulong) || Underlying == typeof(float) ||
        Underlying == typeof(double) || Underlying == typeof(decimal);

    private static bool IsIntegral => IsNumeric && Underlying != typeof(float) && Underlying != typeof(double) && Underlying != typeof(decimal);

    public static Func<TSource, decimal?> ValueSelector(Func<TSource, TKey> selector) => selector switch
    {
        Func<TSource, decimal> f => x => f(x),
        Func<TSource, decimal?> f => x => f(x),
        Func<TSource, int> f => x => f(x),
        Func<TSource, int?> f => x => f(x),
        Func<TSource, long> f => x => f(x),
        Func<TSource, long?> f => x => f(x),
        Func<TSource, short> f => x => f(x),
        Func<TSource, short?> f => x => f(x),
        Func<TSource, byte> f => x => f(x),
        Func<TSource, byte?> f => x => f(x),
        Func<TSource, sbyte> f => x => f(x),
        Func<TSource, sbyte?> f => x => f(x),
        Func<TSource, ushort> f => x => f(x),
        Func<TSource, ushort?> f => x => f(x),
        Func<TSource, uint> f => x => f(x),
        Func<TSource, uint?> f => x => f(x),
        Func<TSource, ulong> f => x => f(x),
        Func<TSource, ulong?> f => x => f(x),
        Func<TSource, double> f => x => FromDouble(f(x)),
        Func<TSource, double?> f => x => FromDouble(f(x)),
        Func<TSource, float> f => x => FromDouble(f(x)),
        Func<TSource, float?> f => x => FromDouble(f(x)),
        _ => throw new InvalidOperationException($"Column type {typeof(TKey)} is not numeric.")
    };

    /// <summary>
    /// Sum/Min/Max go back through the column's formatter (so "$1,234.00" stays "$..."), as does
    /// Avg on non-integral columns. Avg on an integral column would be truncated by that round-trip,
    /// so it is printed with up to two decimals instead.
    /// </summary>
    public static Func<GridAggregate, decimal, string?> Formatter(Func<TKey, string?> columnFormatter)
    {
        var integral = IsIntegral;
        return (function, value) =>
        {
            if (function == GridAggregate.Avg && integral)
            {
                return Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
            }

            return TryConvert(value, out var key)
                ? columnFormatter(key)
                : value.ToString(CultureInfo.InvariantCulture);
        };
    }

    private static decimal? FromDouble(double? value) =>
        value is { } d && double.IsFinite(d) && Math.Abs(d) < 7.9e28 ? (decimal)d : null;

    private static bool TryConvert(decimal value, out TKey key)
    {
        try
        {
            object boxed = Type.GetTypeCode(Underlying) switch
            {
                TypeCode.Decimal => value,
                TypeCode.Double => (double)value,
                TypeCode.Single => (float)value,
                TypeCode.Int32 => checked((int)value),
                TypeCode.Int64 => checked((long)value),
                TypeCode.Int16 => checked((short)value),
                TypeCode.Byte => checked((byte)value),
                TypeCode.SByte => checked((sbyte)value),
                TypeCode.UInt16 => checked((ushort)value),
                TypeCode.UInt32 => checked((uint)value),
                TypeCode.UInt64 => checked((ulong)value),
                _ => throw new InvalidCastException()
            };

            key = (TKey)boxed;
            return true;
        }
        catch (OverflowException)
        {
            key = default!;
            return false;
        }
    }
}
