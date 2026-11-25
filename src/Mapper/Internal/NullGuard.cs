using System.Linq.Expressions;
using System.Reflection;

namespace ArchPillar.Extensions.Mapper.Internal;

/// <summary>
/// Wraps an inlined nested-mapper body in the null guard
/// <c>source is null ? null : body</c>, choosing a null test the LINQ provider
/// translates without comparing the source entity.
/// <para>
/// When the source is a <c>FirstOrDefault</c> / <c>SingleOrDefault</c> call
/// (with or without a predicate), the test projects a scalar marker instead:
/// <c>seq[.Where(predicate)].Select(e =&gt; e == null ? (int?)null : 1).FirstOrDefault() == null</c>.
/// That has exactly the semantics of <c>seq.FirstOrDefault() == null</c>, but EF Core
/// translates it to a scalar <c>(SELECT 1 … LIMIT 1) IS NULL</c> rather than an entity
/// comparison, which it may otherwise translate by joining the subquery ranked with
/// <c>ROW_NUMBER() OVER (PARTITION BY …)</c>. Keeping the <c>x == null ? null : body</c>
/// shape also lets EF Core bind member access through the guard, so a later
/// <c>OrderBy(d =&gt; d.Nested.Name)</c> still translates.
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
        Expression nullTest = TryBuildMarker(source) ?? source;

        return Expression.Condition(
            Expression.Equal(nullTest, Expression.Constant(null, nullTest.Type)),
            Expression.Constant(null, body.Type),
            body);
    }

    private static Expression? TryBuildMarker(Expression source)
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

        if (parameters.Length == 2 && IsLambdaOf(parameters[1].ParameterType, typeof(bool)))
        {
            sequence = Expression.Call(
                FindMethod(declaringType, nameof(Enumerable.Where), 2, type => IsLambdaOf(type, typeof(bool)))
                    .MakeGenericMethod(elementType),
                sequence,
                call.Arguments[1]);
        }
        else if (parameters.Length != 1)
        {
            // The defaultValue overloads can return a non-null fallback; keep the plain null test.
            return null;
        }

        ParameterExpression element = Expression.Parameter(elementType, "element");
        LambdaExpression marker = Expression.Lambda(
            Expression.Condition(
                Expression.Equal(element, Expression.Constant(null, elementType)),
                Expression.Constant(null, typeof(int?)),
                Expression.Constant(1, typeof(int?))),
            element);

        Expression markers = Expression.Call(
            FindMethod(declaringType, nameof(Enumerable.Select), 2, type => IsLambdaOf(type, null))
                .MakeGenericMethod(elementType, typeof(int?)),
            sequence,
            declaringType == typeof(Queryable) ? Expression.Quote(marker) : marker);

        return Expression.Call(
            FindMethod(declaringType, nameof(Enumerable.FirstOrDefault), 1, _ => true).MakeGenericMethod(typeof(int?)),
            markers);
    }

    private static MethodInfo FindMethod(
        Type declaringType, string name, int parameterCount, Func<Type, bool> lastParameter)
        => declaringType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == name
                && m.GetParameters() is { } parameters
                && parameters.Length == parameterCount
                && lastParameter(parameters[^1].ParameterType));

    private static bool IsLambdaOf(Type type, Type? result)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Expression<>))
        {
            type = type.GetGenericArguments()[0];
        }

        return type.IsGenericType
            && type.GetGenericTypeDefinition() == typeof(Func<,>)
            && (result is null || type.GetGenericArguments()[1] == result);
    }
}
