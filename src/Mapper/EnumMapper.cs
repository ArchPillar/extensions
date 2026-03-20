using System.Linq.Expressions;
using ArchPillar.Extensions.Mapper.Internal;

namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// A mapping between two enum types, created via
/// <see cref="MapperContext.CreateEnumMapper{TSource,TDest}"/>.
/// <para>
/// The mapping is defined as a plain C# method (typically a switch expression).
/// The library generates a LINQ-translatable switch expression tree by
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
    /// Returns the mapping as a LINQ expression tree (a switch expression
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
        Expression sourceAsInt = Expression.Convert(sourceParam, sourceUnderlying);

        // Build a flat SwitchExpression instead of nested ConditionalExpressions.
        // Nested conditionals become deeply nested SQL CASE WHEN … ELSE CASE WHEN …
        // which EF Core / Npgsql cannot translate once the enum has many values.
        // A SwitchExpression translates to a single flat SQL CASE expression.
        // Destination constants are kept as enum-typed values so the switch body
        // naturally produces TDest without an outer Convert — some SQL providers
        // (e.g. Npgsql) cannot translate Convert(int, EnumType).
        var cases = new SwitchCase[values.Length];

        for (var i = 0; i < values.Length; i++)
        {
            TSource value = values[i];
            cases[i] = Expression.SwitchCase(
                Expression.Constant(method(value), typeof(TDest)),
                Expression.Constant(Convert.ChangeType(value, sourceUnderlying), sourceUnderlying));
        }

        // default(TDest) is the unreachable fallback — every source value is
        // covered by the switch cases, so this branch is never reached.
        Expression body = Expression.Switch(
            sourceAsInt,
            Expression.Constant(default(TDest), typeof(TDest)),
            cases);

        return Expression.Lambda<Func<TSource, TDest>>(body, sourceParam);
    }
}
