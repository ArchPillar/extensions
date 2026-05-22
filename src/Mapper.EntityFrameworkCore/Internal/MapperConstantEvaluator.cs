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

        Func<object?> accessor = Expression
            .Lambda<Func<object?>>(Expression.Convert(expression, typeof(object)))
            .Compile();
        return accessor();
    }
}
