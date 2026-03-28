using System.Linq.Expressions;
using ArchPillar.Extensions.Mapper.Internal;

namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// A bijective (one-to-one) mapping between two enum types that supports both
/// forward and reverse mapping from a single definition. Created via
/// <see cref="MapperContext.CreateSymmetricEnumMapper{TLeft,TRight}"/>.
/// <para>
/// The mapping is defined as a plain C# method (typically a switch expression)
/// from <typeparamref name="TLeft"/> to <typeparamref name="TRight"/>. The
/// library automatically derives the reverse mapping by inverting the
/// computed pairs.
/// </para>
/// <para>
/// At build time, the mapper validates that the mapping is truly bijective —
/// if two <typeparamref name="TLeft"/> values map to the same
/// <typeparamref name="TRight"/> value, an <see cref="InvalidOperationException"/>
/// is thrown listing the conflict.
/// </para>
/// <para>
/// Can be used standalone or inlined into parent mapper expressions:
/// </para>
/// <code>
/// // Forward:
/// Order = CreateMapper&lt;Order, OrderDto&gt;(src =&gt; new OrderDto
/// {
///     Status = StatusMapper.Map(src.Status),
/// });
///
/// // Reverse:
/// OrderFromDto = CreateMapper&lt;OrderDto, Order&gt;(dto =&gt; new Order
/// {
///     Status = StatusMapper.MapReverse(dto.Status),
/// });
/// </code>
/// </summary>
/// <typeparam name="TLeft">The left enum type.</typeparam>
/// <typeparam name="TRight">The right enum type.</typeparam>
public sealed class SymmetricEnumMapper<TLeft, TRight> : IMapper, IReversibleMapper
    where TLeft  : struct, Enum
    where TRight : struct, Enum
{
    internal SymmetricEnumMapper(Func<TLeft, TRight> forwardMethod)
    {
        ValidateBijection(forwardMethod);

        Forward = new EnumMapper<TLeft, TRight>(forwardMethod);
        Reverse = new EnumMapper<TRight, TLeft>(BuildReverseMethod(forwardMethod));
    }

    // -----------------------------------------------------------------
    // Forward: TLeft → TRight
    // -----------------------------------------------------------------

    /// <summary>
    /// Maps a <typeparamref name="TLeft"/> value to <typeparamref name="TRight"/>.
    /// </summary>
    public TRight Map(TLeft source) => Forward.Map(source);

    /// <summary>
    /// Maps a nullable <typeparamref name="TLeft"/> value. Returns
    /// <see langword="null"/> when <paramref name="source"/> is <see langword="null"/>.
    /// </summary>
    public TRight? Map(TLeft? source) => Forward.Map(source);

    /// <summary>
    /// Maps a nullable <typeparamref name="TLeft"/> value. Returns
    /// <paramref name="defaultValue"/> when <paramref name="source"/> is
    /// <see langword="null"/>.
    /// </summary>
    public TRight Map(TLeft? source, TRight defaultValue)
        => Forward.Map(source, defaultValue);

    // -----------------------------------------------------------------
    // Reverse: TRight → TLeft
    // -----------------------------------------------------------------

    /// <summary>
    /// Maps a <typeparamref name="TRight"/> value back to
    /// <typeparamref name="TLeft"/>.
    /// </summary>
    public TLeft MapReverse(TRight source) => Reverse.Map(source);

    /// <summary>
    /// Maps a nullable <typeparamref name="TRight"/> value back to
    /// <typeparamref name="TLeft"/>. Returns <see langword="null"/> when
    /// <paramref name="source"/> is <see langword="null"/>.
    /// </summary>
    public TLeft? MapReverse(TRight? source) => Reverse.Map(source);

    /// <summary>
    /// Maps a nullable <typeparamref name="TRight"/> value back to
    /// <typeparamref name="TLeft"/>. Returns <paramref name="defaultValue"/>
    /// when <paramref name="source"/> is <see langword="null"/>.
    /// </summary>
    public TLeft MapReverse(TRight? source, TLeft defaultValue)
        => Reverse.Map(source, defaultValue);

    // -----------------------------------------------------------------
    // Expression access
    // -----------------------------------------------------------------

    /// <summary>
    /// Returns the forward mapping as a LINQ expression tree.
    /// </summary>
    public Expression<Func<TLeft, TRight>> ToExpression()
        => Forward.ToExpression();

    /// <summary>
    /// Returns the forward nullable-to-nullable mapping as a LINQ expression tree.
    /// </summary>
    public Expression<Func<TLeft?, TRight?>> ToNullableExpression()
        => Forward.ToNullableExpression();

    /// <summary>
    /// Returns the reverse mapping as a LINQ expression tree.
    /// </summary>
    public Expression<Func<TRight, TLeft>> ToReverseExpression()
        => Reverse.ToExpression();

    /// <summary>
    /// Returns the reverse nullable-to-nullable mapping as a LINQ expression tree.
    /// </summary>
    public Expression<Func<TRight?, TLeft?>> ToReverseNullableExpression()
        => Reverse.ToNullableExpression();

    // -----------------------------------------------------------------
    // Inner mapper access
    // -----------------------------------------------------------------

    /// <summary>
    /// The inner forward <see cref="EnumMapper{TSource,TDest}"/>.
    /// </summary>
    public EnumMapper<TLeft, TRight> Forward { get; }

    /// <summary>
    /// The inner reverse <see cref="EnumMapper{TSource,TDest}"/>.
    /// </summary>
    public EnumMapper<TRight, TLeft> Reverse { get; }

    // -----------------------------------------------------------------
    // IMapper / IReversibleMapper
    // -----------------------------------------------------------------

    LambdaExpression IMapper.GetRawExpression(IncludeSet includes, int depth)
        => ToExpression();

    void IMapper.Compile()
    {
        _ = ToExpression();
        _ = ToReverseExpression();
    }

    LambdaExpression IReversibleMapper.GetReverseRawExpression(IncludeSet includes, int depth)
        => ToReverseExpression();

    // -----------------------------------------------------------------
    // Validation & reverse derivation
    // -----------------------------------------------------------------

    private static void ValidateBijection(Func<TLeft, TRight> forwardMethod)
    {
        var seen = new Dictionary<TRight, TLeft>();

        foreach (TLeft value in Enum.GetValues<TLeft>())
        {
            TRight mapped = forwardMethod(value);

            if (seen.TryGetValue(mapped, out TLeft existing))
            {
                throw new InvalidOperationException(
                    $"SymmetricEnumMapper<{typeof(TLeft).Name}, {typeof(TRight).Name}> requires a " +
                    $"one-to-one mapping, but both '{existing}' and '{value}' map to '{mapped}'. " +
                    "Use EnumMapper<,> instead for many-to-one mappings.");
            }

            seen[mapped] = value;
        }
    }

    private static Func<TRight, TLeft> BuildReverseMethod(Func<TLeft, TRight> forwardMethod)
    {
        var reverseMap = new Dictionary<TRight, TLeft>();

        foreach (TLeft value in Enum.GetValues<TLeft>())
        {
            reverseMap[forwardMethod(value)] = value;
        }

        return right => reverseMap[right];
    }
}
