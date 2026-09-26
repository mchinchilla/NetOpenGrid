using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Domain.Results;

namespace NetOpenGrid.Persistence;

/// <summary>
/// Grand-total aggregates pushed down as a single query:
/// <c>source.GroupBy(x =&gt; 1).Select(g =&gt; new object[] { g.Sum(..), g.Average(..), .. })</c>,
/// i.e. one <c>SELECT SUM(..), AVG(..), MIN(..), MAX(..)</c>. The projection only depends on the
/// columns, so it is built once per data source and reused for every request.
/// Integral columns are widened to <c>long?</c> so a SUM cannot overflow the column's own type;
/// everything is nullable so an empty set yields NULLs instead of throwing.
/// </summary>
internal sealed class EFGridAggregateProjection<T>
{
    private readonly (string Field, GridAggregate Function)[] _slots;
    private readonly Expression<Func<IGrouping<int, T>, object?[]>> _projection;
    private readonly Expression<Func<T, int>> _constantKey = static _ => 1;

    private EFGridAggregateProjection((string, GridAggregate)[] slots, Expression<Func<IGrouping<int, T>, object?[]>> projection)
    {
        _slots = slots;
        _projection = projection;
    }

    /// <summary>Null when no column with an expression selector asks for aggregates.</summary>
    public static EFGridAggregateProjection<T>? TryCreate(IReadOnlyList<GridColumn<T>> columns)
    {
        var group = Expression.Parameter(typeof(IGrouping<int, T>), "g");
        var slots = new List<(string, GridAggregate)>();
        var values = new List<Expression>();

        foreach (var column in columns)
        {
            if (column.SelectorExpression is not { } selector)
            {
                continue;
            }

            var widened = WidenedType(selector.Body.Type);
            var parameter = selector.Parameters[0];
            var element = Expression.Lambda(Expression.Convert(selector.Body, widened), parameter);

            foreach (var function in column.Aggregates.Functions())
            {
                var method = function switch
                {
                    GridAggregate.Sum => Enumerable(nameof(System.Linq.Enumerable.Sum), widened),
                    GridAggregate.Avg => Enumerable(nameof(System.Linq.Enumerable.Average), widened),
                    GridAggregate.Min => Enumerable(nameof(System.Linq.Enumerable.Min), widened),
                    _ => Enumerable(nameof(System.Linq.Enumerable.Max), widened)
                };

                values.Add(Expression.Convert(Expression.Call(method, group, element), typeof(object)));
                slots.Add((column.Field, function));
            }
        }

        if (values.Count == 0)
        {
            return null;
        }

        var projection = Expression.Lambda<Func<IGrouping<int, T>, object?[]>>(
            Expression.NewArrayInit(typeof(object), values), group);

        return new EFGridAggregateProjection<T>(slots.ToArray(), projection);
    }

    public async ValueTask<GridAggregates> ExecuteAsync(IQueryable<T> filtered, CancellationToken cancellationToken)
    {
        var row = await filtered
            .GroupBy(_constantKey)
            .Select(_projection)
            .FirstOrDefaultAsync(cancellationToken);

        var byField = new Dictionary<string, (decimal? Sum, decimal? Avg, decimal? Min, decimal? Max)>(StringComparer.Ordinal);
        for (var i = 0; i < _slots.Length; i++)
        {
            var (field, function) = _slots[i];
            var value = ToDecimal(row?[i]);
            var current = byField.GetValueOrDefault(field);

            byField[field] = function switch
            {
                GridAggregate.Sum => current with { Sum = value },
                GridAggregate.Avg => current with { Avg = value },
                GridAggregate.Min => current with { Min = value },
                _ => current with { Max = value }
            };
        }

        return new GridAggregates(byField.ToDictionary(
            static kv => kv.Key,
            static kv => new GridAggregateValues(kv.Value.Sum, kv.Value.Avg, kv.Value.Min, kv.Value.Max),
            StringComparer.Ordinal));
    }

    private static Type WidenedType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return Type.GetTypeCode(underlying) switch
        {
            TypeCode.Decimal => typeof(decimal?),
            TypeCode.Double or TypeCode.Single => typeof(double?),
            _ => typeof(long?)
        };
    }

    /// <summary>The <c>Enumerable.X&lt;T&gt;(IEnumerable&lt;T&gt;, Func&lt;T, widened&gt;)</c> overload.</summary>
    private static MethodInfo Enumerable(string name, Type widened) =>
        typeof(System.Linq.Enumerable)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == name &&
                         m.IsGenericMethodDefinition &&
                         m.GetGenericArguments().Length == 1 &&
                         m.GetParameters() is [_, var selector] &&
                         selector.ParameterType.IsGenericType &&
                         selector.ParameterType.GetGenericTypeDefinition() == typeof(Func<,>) &&
                         selector.ParameterType.GetGenericArguments()[1] == widened)
            .MakeGenericMethod(typeof(T));

    private static decimal? ToDecimal(object? value) => value switch
    {
        null => null,
        decimal d => d,
        long l => l,
        double d when double.IsFinite(d) && Math.Abs(d) < 7.9e28 => (decimal)d,
        double => null,
        _ => Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture)
    };
}
