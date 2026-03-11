using System.Linq.Expressions;
using ArchPillar.Mapper.Internal;

namespace ArchPillar.Mapper;

/// <summary>
/// A mapping between two enum types, created via
/// <see cref="MapperContext.CreateEnumMapper{TSource,TDest}"/>.
///
/// The mapping is defined as a plain C# method (typically a switch expression).
/// The library generates a LINQ-translatable conditional expression tree by
/// calling the method for every possible <typeparamref name="TSource"/> value
/// and recording the output.
///
/// Can be used standalone or inlined into a parent mapper's expression:
/// <code>
/// Order = CreateMapper&lt;Order, OrderDto&gt;(src => new OrderDto
/// {
///     Status = OrderStatus.Map(src.Status),
/// });
/// </code>
/// </summary>
/// <typeparam name="TSource">The source enum type.</typeparam>
/// <typeparam name="TDest">The destination enum type.</typeparam>
public sealed class EnumMapper<TSource, TDest>(Func<TSource, TDest> mappingMethod)
    : IMapper
    where TSource : struct, Enum
    where TDest   : struct, Enum
{
    private readonly Lazy<Expression<Func<TSource, TDest>>> _expression =
        new(() => BuildExpression(mappingMethod));

    /// <summary>
    /// Maps a single source enum value to the destination enum type.
    /// </summary>
    public TDest Map(TSource source)
        => mappingMethod(source);

    /// <summary>
    /// Returns the mapping as a LINQ expression tree (a chain of conditional
    /// expressions covering every <typeparamref name="TSource"/> value).
    /// </summary>
    public Expression<Func<TSource, TDest>> ToExpression()
        => _expression.Value;

    LambdaExpression IMapper.GetExpression(IncludeSet includes, IReadOnlyDictionary<object, object?> variableBindings, bool nullSafeOptionals)
        => ToExpression();

    private static Expression<Func<TSource, TDest>> BuildExpression(Func<TSource, TDest> method)
    {
        var sourceParam = Expression.Parameter(typeof(TSource), "source");

        // Build the throw branch for the unreachable else (invalid enum value at runtime)
        var throwBranch = Expression.Throw(
            Expression.New(
                typeof(ArgumentOutOfRangeException).GetConstructor(
                    [typeof(string), typeof(object), typeof(string)])!,
                Expression.Constant("source"),
                Expression.Convert(sourceParam, typeof(object)),
                Expression.Constant(null, typeof(string))),
            typeof(TDest));

        // Build the conditional chain from last value to first so the
        // first value ends up outermost: V1 ? D1 : V2 ? D2 : ... : throw
        Expression body = throwBranch;
        foreach (var value in Enum.GetValues<TSource>().Reverse())
        {
            body = Expression.Condition(
                Expression.Equal(sourceParam, Expression.Constant(value)),
                Expression.Constant(method(value)),
                body);
        }

        return Expression.Lambda<Func<TSource, TDest>>(body, sourceParam);
    }
}
