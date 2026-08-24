using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Domain.GridQuerying;

namespace NetOpenGrid.Persistence;

/// <summary>
/// Translates grid columns (kept expressions + non-generic value parsers) into
/// IQueryable expression trees. Value literals are parsed once with the same
/// invariant rules as the in-memory pipeline and embedded as constants.
/// </summary>
internal static class EFGridExpressionTranslator
{
    private static readonly MethodInfo LikeMethod = typeof(DbFunctionsExtensions)
        .GetMethod(nameof(DbFunctionsExtensions.Like), [typeof(DbFunctions), typeof(string), typeof(string), typeof(string)])
        ?? throw new InvalidOperationException("EF.Functions.Like(match, pattern, escape) was not found.");

    public static Expression<Func<T, bool>>? CreateFilter<T>(GridColumn<T> column, FilterOperator op, string? rawValue)
    {
        var lambda = column.SelectorExpression;
        if (lambda is null)
        {
            return null;
        }

        var parameter = (ParameterExpression)lambda.Parameters[0];
        var body = lambda.Body;
        var raw = rawValue ?? string.Empty;

        switch (op)
        {
            case FilterOperator.IsEmpty when !body.Type.IsValueType:
                return BuildLambda<T>(IsEmptyBody(body), parameter);
            case FilterOperator.IsNotEmpty when !body.Type.IsValueType:
                return BuildLambda<T>(Expression.Not(IsEmptyBody(body)), parameter);
            case FilterOperator.IsEmpty or FilterOperator.IsNotEmpty:
                return null;
        }

        if (body.Type == typeof(string))
        {
            var pattern = op switch
            {
                FilterOperator.Contains => $"%{EscapeLike(raw)}%",
                FilterOperator.StartsWith => $"{EscapeLike(raw)}%",
                FilterOperator.EndsWith => $"%{EscapeLike(raw)}",
                FilterOperator.Equals => EscapeLike(raw),
                FilterOperator.NotEquals => EscapeLike(raw),
                _ => null
            };

            if (pattern is not null)
            {
                var like = BuildLike(body, pattern);
                return BuildLambda<T>(op == FilterOperator.NotEquals ? Expression.Not(like) : like, parameter);
            }
        }

        if (column.FilterValueParser is null)
        {
            return null;
        }

        if (!column.FilterValueParser(raw, out var parsed) || parsed is null)
        {
            return BuildLambda<T>(Expression.Constant(false), parameter);
        }

        var constant = Expression.Constant(parsed, body.Type);
        Expression? comparison = op switch
        {
            FilterOperator.Equals => Expression.Equal(body, constant),
            FilterOperator.NotEquals => Expression.NotEqual(body, constant),
            FilterOperator.GreaterThan => Expression.GreaterThan(body, constant),
            FilterOperator.GreaterThanOrEqual => Expression.GreaterThanOrEqual(body, constant),
            FilterOperator.LessThan => Expression.LessThan(body, constant),
            FilterOperator.LessThanOrEqual => Expression.LessThanOrEqual(body, constant),
            _ => null
        };

        return comparison is null ? null : BuildLambda<T>(comparison, parameter);
    }

    public static Expression<Func<T, bool>>? CreateSearch<T>(IReadOnlyList<GridColumn<T>> columns, string term)
    {
        ParameterExpression? parameter = null;
        Expression? combined = null;
        var pattern = $"%{EscapeLike(term)}%";

        foreach (var column in columns)
        {
            if (column.SearchStrategy is null || column.SelectorExpression is null)
            {
                continue;
            }

            if (column.SelectorExpression.ReturnType != typeof(string))
            {
                continue;
            }

            var lambda = column.SelectorExpression;
            var lambdaParameter = (ParameterExpression)lambda.Parameters[0];
            parameter ??= lambdaParameter;
            var body = RebindParameter(lambda.Body, lambdaParameter, parameter);
            var like = BuildLike(body, pattern);
            combined = combined is null ? like : Expression.OrElse(combined, like);
        }

        return combined is null || parameter is null ? null : Expression.Lambda<Func<T, bool>>(combined, parameter);
    }

    /// <summary>
    /// Keys are boxed to object (EF strips the Convert node when translating);
    /// this keeps the composition generic without reflection or dynamic dispatch.
    /// </summary>
    public static IQueryable<T> ApplySort<T>(IQueryable<T> source, GridColumn<T> column, SortDirection direction, bool thenBy)
    {
        var lambda = column.SelectorExpression!;
        var key = Expression.Lambda<Func<T, object?>>(
            Expression.Convert(lambda.Body, typeof(object)),
            (ParameterExpression)lambda.Parameters[0]);

        return (thenBy, direction == SortDirection.Descending) switch
        {
            (false, false) => source.OrderBy(key),
            (false, true) => source.OrderByDescending(key),
            (true, false) => ((IOrderedQueryable<T>)source).ThenBy(key),
            (true, true) => ((IOrderedQueryable<T>)source).ThenByDescending(key)
        };
    }

    private static Expression IsEmptyBody(Expression body)
    {
        var nullCheck = Expression.Equal(body, Expression.Constant(null, body.Type));
        return body.Type == typeof(string)
            ? Expression.OrElse(nullCheck, Expression.Equal(body, Expression.Constant(string.Empty)))
            : nullCheck;
    }

    private static MethodCallExpression BuildLike(Expression body, string pattern) =>
        Expression.Call(
            LikeMethod,
            Expression.Property(null, typeof(EF).GetProperty(nameof(EF.Functions))!),
            body,
            Expression.Constant(pattern),
            Expression.Constant("\\", typeof(string)));

    private static Expression<Func<T, bool>> BuildLambda<T>(Expression body, ParameterExpression parameter) =>
        Expression.Lambda<Func<T, bool>>(body, parameter);

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static Expression RebindParameter(Expression body, ParameterExpression from, ParameterExpression to) =>
        new ParameterReplacer(from, to).Visit(body);

    private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == from ? to : base.VisitParameter(node);
    }
}
