using System.Linq.Expressions;

namespace ArchPillar.Extensions.Mapper.EntityFrameworkCore.Internal;

/// <summary>
/// Evaluates an expression that references no query parameters — a mapper
/// accessor (kept as a constant by <see cref="MapperEvaluatableExpressionFilterPlugin"/>)
/// or an options lambda captured from the surrounding closure — by compiling and
/// invoking it.
/// </summary>
internal static class MapperConstantEvaluator
{
    public static object? Evaluate(Expression expression)
    {
        if (expression is ConstantExpression constant)
        {
            return constant.Value;
        }

        Func<object?> accessor = Compile(expression);
        return accessor();
    }

    private static Func<object?> Compile(Expression expression)
    {
        try
        {
            return Expression
                .Lambda<Func<object?>>(Expression.Convert(expression, typeof(object)))
                .Compile();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            // Which of the two is thrown depends on the EF Core version: both mean the expression reaches a query
            // parameter, which has no value until the query runs.
            throw new InvalidOperationException(
                "A mapper call in this query uses a value captured in a closure, such as a local variable or a " +
                "method parameter, which cannot be read once EF has lifted it into a query parameter. Call " +
                ".ApplyMappers() on the query, so the mapper is resolved before parameter extraction.",
                exception);
        }
    }
}
