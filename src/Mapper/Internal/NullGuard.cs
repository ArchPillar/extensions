using System.Linq.Expressions;
using System.Reflection;

namespace ArchPillar.Extensions.Mapper.Internal;

/// <summary>
/// Wraps an inlined nested-mapper body in the null guard
/// <c>source is null ? null : body</c>, choosing a test the LINQ provider
/// translates without materialising the source entity.
/// <para>
/// When the source is a <c>FirstOrDefault</c> / <c>SingleOrDefault</c> call
/// (with or without a predicate), the null test becomes an existence check,
/// <c>!seq[.Where(predicate)].Take(1).Any(e =&gt; e != null)</c>. That has exactly the
/// semantics of <c>seq.FirstOrDefault() == null</c>, but EF Core translates it to
/// <c>NOT EXISTS (… LIMIT 1)</c> instead of an entity comparison, which it can only
/// translate by joining the subquery ranked with <c>ROW_NUMBER() OVER (PARTITION BY …)</c>.
/// </para>
/// <para>
/// Any other source is compared against a typed null constant rather than a
/// <see cref="DefaultExpression"/>: EF Core's entity-equality rewrite tries to read
/// the key off a <c>DefaultExpression</c> node (<c>EF.Property&lt;TKey&gt;(default(Nav), "Id")</c>),
/// which fails to translate when the key uses a value converter, while
/// <c>Expression.Constant(null, type)</c> folds cleanly into an FK <c>IS NULL</c> check.
/// </para>
/// </summary>
internal static class NullGuard
{
    internal static Expression Wrap(Expression source, Expression body)
    {
        Expression isNull = TryBuildEmptyCheck(source)
            ?? Expression.Equal(source, Expression.Constant(null, source.Type));

        return Expression.Condition(isNull, Expression.Constant(null, body.Type), body);
    }

    private static Expression? TryBuildEmptyCheck(Expression source)
    {
        if (source is not MethodCallExpression { Method: { IsGenericMethod: true } method } call
            || (method.DeclaringType != typeof(Enumerable) && method.DeclaringType != typeof(Queryable))
            || (method.Name != nameof(Enumerable.FirstOrDefault) && method.Name != nameof(Enumerable.SingleOrDefault)))
        {
            return null;
        }

        Type declaringType = method.DeclaringType;
        Type elementType   = method.GetGenericArguments()[0];
        ParameterInfo[] parameters = method.GetParameters();
        Expression sequence = call.Arguments[0];

        if (parameters.Length == 2 && IsPredicate(parameters[1].ParameterType))
        {
            sequence = Expression.Call(
                FindMethod(declaringType, nameof(Enumerable.Where), IsPredicate).MakeGenericMethod(elementType),
                sequence,
                call.Arguments[1]);
        }
        else if (parameters.Length != 1)
        {
            // The defaultValue overloads can return a non-null fallback; keep the plain null test.
            return null;
        }

        sequence = Expression.Call(
            FindMethod(declaringType, nameof(Enumerable.Take), type => type == typeof(int)).MakeGenericMethod(elementType),
            sequence,
            Expression.Constant(1));

        ParameterExpression element = Expression.Parameter(elementType, "element");
        LambdaExpression notNull = Expression.Lambda(
            Expression.NotEqual(element, Expression.Constant(null, elementType)),
            element);

        return Expression.Not(Expression.Call(
            FindMethod(declaringType, nameof(Enumerable.Any), IsPredicate).MakeGenericMethod(elementType),
            sequence,
            declaringType == typeof(Queryable) ? Expression.Quote(notNull) : notNull));
    }

    private static MethodInfo FindMethod(Type declaringType, string name, Func<Type, bool> secondParameter)
        => declaringType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == name
                && m.GetParameters() is { Length: 2 } parameters
                && secondParameter(parameters[1].ParameterType));

    private static bool IsPredicate(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Expression<>))
        {
            type = type.GetGenericArguments()[0];
        }

        return type.IsGenericType
            && type.GetGenericTypeDefinition() == typeof(Func<,>)
            && type.GetGenericArguments()[1] == typeof(bool);
    }
}
