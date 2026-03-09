using System.Linq.Expressions;

namespace ArchPillar.Mapper;

/// <summary>
/// Fluent builder for configuring a <see cref="Mapper{TSource,TDest}"/>.
/// Every destination property must be covered by exactly one of
/// <see cref="Map{TValue}"/>, <see cref="Optional{TValue}"/>, or
/// <see cref="Ignore{TValue}"/>; any unaccounted property causes an
/// <see cref="InvalidOperationException"/> when the mapper is built.
///
/// Instances are obtained from <see cref="MapperContext.CreateMapper{TSource,TDest}"/>.
/// Assigning the builder to a <see cref="Mapper{TSource,TDest}"/> property
/// triggers <see cref="Build"/> implicitly via the conversion operator.
/// </summary>
public abstract class MapperBuilder<TSource, TDest>
{
    /// <summary>
    /// Maps a destination property from a source expression.
    /// Included in both in-memory mapping and LINQ projection.
    /// </summary>
    public abstract MapperBuilder<TSource, TDest> Map<TValue>(
        Expression<Func<TDest, TValue>> dest,
        Expression<Func<TSource, TValue>> src);

    /// <summary>
    /// Declares an opt-in property excluded from the default mapping.
    /// Must be explicitly requested at the call site via
    /// <see cref="MapOptions{TDest}.Include{TValue}(Expression{Func{TDest,TValue}})"/>
    /// or the string-path overload.
    /// </summary>
    public abstract MapperBuilder<TSource, TDest> Optional<TValue>(
        Expression<Func<TDest, TValue>> dest,
        Expression<Func<TSource, TValue>> src);

    /// <summary>
    /// Marks a destination property as intentionally unmapped.
    /// Required for any property not covered by <see cref="Map{TValue}"/>
    /// or <see cref="Optional{TValue}"/> so the builder can verify full coverage.
    /// </summary>
    public abstract MapperBuilder<TSource, TDest> Ignore<TValue>(
        Expression<Func<TDest, TValue>> dest);

    /// <summary>
    /// Finalizes configuration and returns the built mapper.
    /// Throws <see cref="InvalidOperationException"/> if any destination
    /// property is unaccounted for.
    /// </summary>
    public abstract Mapper<TSource, TDest> Build();

    /// <summary>
    /// Allows assigning a builder directly to a <see cref="Mapper{TSource,TDest}"/>
    /// property without an explicit <see cref="Build"/> call.
    /// </summary>
    public static implicit operator Mapper<TSource, TDest>(MapperBuilder<TSource, TDest> builder)
        => builder.Build();
}
