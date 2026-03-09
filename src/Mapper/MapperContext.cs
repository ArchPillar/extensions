using System.Linq.Expressions;

namespace ArchPillar.Mapper;

/// <summary>
/// Abstract base class that groups related mappers and variables into a
/// single, injectable unit — analogous to how <c>DbContext</c> groups
/// entity sets in EF Core.
///
/// Subclass this and declare mappers and variables as public properties.
/// Initialize them in the constructor using the protected factory methods.
///
/// <code>
/// public class AppMappers : MapperContext
/// {
///     public Variable&lt;int&gt; CurrentUserId { get; } = CreateVariable&lt;int&gt;();
///
///     public Mapper&lt;Order, OrderDto&gt; Order { get; }
///
///     public AppMappers() : base(o => o.EagerBuild = false)
///     {
///         Order = CreateMapper&lt;Order, OrderDto&gt;(src => new OrderDto
///         {
///             Id      = src.Id,
///             IsOwner = src.OwnerId == CurrentUserId,
///         });
///     }
/// }
/// </code>
///
/// Multiple <see cref="MapperContext"/> subclasses can be composed via
/// constructor injection without any library involvement:
/// <code>
/// public class AppMappers
/// {
///     public OrderMappers Orders { get; }
///     public ProductMappers Products { get; }
///
///     public AppMappers(OrderMappers orders, ProductMappers products)
///         => (Orders, Products) = (orders, products);
/// }
/// </code>
/// </summary>
public abstract class MapperContext
{
    /// <summary>
    /// Initializes the context with default options (lazy build).
    /// </summary>
    protected MapperContext()
        : this(_ => { })
    { }

    /// <summary>
    /// Initializes the context with a configuration delegate.
    /// <code>
    /// public AppMappers() : base(o => o.EagerBuild = true) { ... }
    /// </code>
    /// </summary>
    protected MapperContext(Action<MapperContextOptions> configure)
        => throw new NotImplementedException();

    /// <summary>
    /// Initializes the context with a pre-built options instance,
    /// suitable for injection from a DI container.
    /// <code>
    /// services.AddSingleton(new MapperContextOptions { EagerBuild = true });
    /// services.AddSingleton&lt;AppMappers&gt;();
    /// </code>
    /// </summary>
    protected MapperContext(MapperContextOptions options)
        => throw new NotImplementedException();

    // -------------------------------------------------------------------------
    // Factory methods
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a mapper builder for <typeparamref name="TSource"/> →
    /// <typeparamref name="TDest"/>.
    ///
    /// Optionally supply a member-init expression that covers the straightforward
    /// properties. Additional <c>.Map()</c>, <c>.Optional()</c>, and
    /// <c>.Ignore()</c> calls can be chained to extend or complete coverage.
    /// The builder tracks which destination properties have been accounted for
    /// and throws at build time if any remain unhandled.
    ///
    /// Assigning the builder to a <see cref="Mapper{TSource,TDest}"/> property
    /// triggers <see cref="MapperBuilder{TSource,TDest}.Build"/> implicitly via
    /// the conversion operator.
    /// </summary>
    protected MapperBuilder<TSource, TDest> CreateMapper<TSource, TDest>(
        Expression<Func<TSource, TDest>>? memberInitExpression = null)
        => throw new NotImplementedException();

    /// <summary>
    /// Creates an enum mapper from a plain mapping method.
    /// The library enumerates every <typeparamref name="TSource"/> value,
    /// calls <paramref name="mappingMethod"/> for each, and builds a
    /// LINQ-translatable conditional expression tree from the results.
    /// </summary>
    protected EnumMapper<TSource, TDest> CreateEnumMapper<TSource, TDest>(
        Func<TSource, TDest> mappingMethod)
        where TSource : struct, Enum
        where TDest   : struct, Enum
        => throw new NotImplementedException();

    /// <summary>
    /// Creates a typed <see cref="Variable{T}"/> that can be used inside
    /// mapping expressions as a placeholder value. Declare the returned
    /// instance as a public property on the context so callers can reference
    /// it by name — no magic strings, full IDE navigation.
    /// </summary>
    protected static Variable<T> CreateVariable<T>()
        => throw new NotImplementedException();
}
