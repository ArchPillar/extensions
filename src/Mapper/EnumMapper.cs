using System.Linq.Expressions;
using ArchPillar.Extensions.Mapper.Internal;

namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// A mapping between two enum types, created via
/// <see cref="MapperContext.CreateEnumMapper{TSource,TDest}"/>.
/// <para>
/// The mapping is defined as a plain C# method (typically a switch expression).
/// The library generates a LINQ-translatable conditional expression tree by
/// calling the method for every possible <typeparamref name="TSource"/> value
/// and recording the output.
/// </para>
/// <para>
/// Can be used standalone or inlined into a parent mapper's expression:
/// </para>
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
    /// Returns the mapping as a LINQ expression tree (a conditional chain
    /// covering every <typeparamref name="TSource"/> value).
    /// </summary>
    public Expression<Func<TSource, TDest>> ToExpression()
        => _expression.Value;

    LambdaExpression IMapper.GetRawExpression(IncludeSet includes, int depth)
    {
        return ToExpression();
    }

    void IMapper.Compile()
    {
        _ = ToExpression();
    }

    private static Expression<Func<TSource, TDest>> BuildExpression(Func<TSource, TDest> method)
    {
        ParameterExpression sourceParam = Expression.Parameter(typeof(TSource), "source");
        TSource[] values = Enum.GetValues<TSource>();

        // Cast the source enum to its underlying integer type so that LINQ
        // providers (EF Core, etc.) can translate the equality checks to SQL.
        // Bare enum comparisons are not translatable by most providers.
        Type sourceUnderlying = Enum.GetUnderlyingType(typeof(TSource));
        Type destUnderlying = Enum.GetUnderlyingType(typeof(TDest));
        Expression sourceAsInt = Expression.Convert(sourceParam, sourceUnderlying);

        // Build a conditional chain whose branches are plain integer constants
        // (the underlying values of TDest), then wrap the whole chain in a
        // single Convert to TDest.  Using the underlying integer type throughout
        // the chain avoids enum-typed ConstantExpressions that some LINQ
        // providers (EF Core + Npgsql, etc.) fail to translate when the number
        // of enum members is large.
        Expression body = Expression.Constant(
            Convert.ChangeType(default(TDest), destUnderlying), destUnderlying);

        for (var i = values.Length - 1; i >= 0; i--)
        {
            TSource value = values[i];
            TDest mapped = method(value);
            body = Expression.Condition(
                Expression.Equal(
                    sourceAsInt,
                    Expression.Constant(Convert.ChangeType(value, sourceUnderlying), sourceUnderlying)),
                Expression.Constant(Convert.ChangeType(mapped, destUnderlying), destUnderlying),
                body);
        }

        // Single outer Convert from the underlying integer type to TDest.
        // EF Core stores enums as their underlying integer type by default,
        // so this translates to a no-op (or trivial implicit cast) in SQL.
        body = Expression.Convert(body, typeof(TDest));

        return Expression.Lambda<Func<TSource, TDest>>(body, sourceParam);
    }
}
