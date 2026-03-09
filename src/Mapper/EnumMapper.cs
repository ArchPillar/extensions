using System.Linq.Expressions;

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
public sealed class EnumMapper<TSource, TDest>
    where TSource : struct, Enum
    where TDest   : struct, Enum
{
    /// <summary>
    /// Maps a single source enum value to the destination enum type.
    /// </summary>
    public TDest Map(TSource source)
        => throw new NotImplementedException();

    /// <summary>
    /// Returns the mapping as a LINQ expression tree (a chain of conditional
    /// expressions covering every <typeparamref name="TSource"/> value).
    /// </summary>
    public Expression<Func<TSource, TDest>> ToExpression()
        => throw new NotImplementedException();
}
