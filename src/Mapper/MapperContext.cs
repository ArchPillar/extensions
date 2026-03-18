using System.Linq.Expressions;
using System.Reflection;
using ArchPillar.Extensions.Mapper.Internal;

namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// Abstract base class that groups related mappers and variables into a
/// single, injectable unit — analogous to how <c>DbContext</c> groups
/// entity sets in EF Core.
/// <para>
/// Subclass this and declare mappers and variables as public properties.
/// Initialize them in the constructor using the protected factory methods.
/// </para>
/// <code>
/// public class AppMappers : MapperContext
/// {
///     public Variable&lt;int&gt; CurrentUserId { get; } = CreateVariable&lt;int&gt;();
///
///     public Mapper&lt;Order, OrderDto&gt; Order { get; }
///
///     public AppMappers()
///     {
///         Order = CreateMapper&lt;Order, OrderDto&gt;(src => new OrderDto
///         {
///             Id      = src.Id,
///             IsOwner = src.OwnerId == CurrentUserId,
///         });
///     }
/// }
/// </code>
/// <para>
/// Multiple <see cref="MapperContext"/> subclasses can be composed via
/// constructor injection without any library involvement:
/// </para>
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
/// <para>
/// Nested mappers can also be retrieved via method calls — the expression visitor
/// detects and inlines them automatically. Methods may take no arguments or only
/// constant arguments:
/// </para>
/// <code>
/// public class OrderMappers : MapperContext
/// {
///     public OrderMappers(CustomerMappers customers)
///     {
///         Order = CreateMapper&lt;Order, OrderDto&gt;(src => new OrderDto
///         {
///             Customer = customers.GetMapper().Map(src.Customer),
///         });
///     }
/// }
/// </code>
/// </summary>
public abstract class MapperContext
{
    // -------------------------------------------------------------------------
    // Coverage validation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Gets the default <see cref="CoverageValidation"/> mode applied to all
    /// mappers created by this context. Override in a subclass to change the
    /// default for all mappers in the context. Individual mappers can further
    /// override via <see cref="MapperBuilder{TSource,TDest}.SetCoverageValidation"/>.
    /// </summary>
    protected virtual CoverageValidation DefaultCoverageValidation
        => CoverageValidation.NonNullableProperties;

    // -------------------------------------------------------------------------
    // AOT source generator hook
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called at the end of the constructor to allow the AOT source generator to
    /// wire pre-compiled mapping delegates into the mapper instances. When the
    /// subclass is declared <c>partial</c>, the generator emits an implementation
    /// that calls <see cref="Mapper{TSource,TDest}.SetCompiledDelegates"/> for
    /// each mapper property, eliminating the need for <c>Expression.Compile()</c>.
    /// </summary>
    protected virtual void OnAotInitialize() { }

    // -------------------------------------------------------------------------
    // Factory methods
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a mapper builder for <typeparamref name="TSource"/> →
    /// <typeparamref name="TDest"/>.
    /// <para>
    /// Optionally supply a member-init expression that covers the straightforward
    /// properties. Additional <c>.Map()</c>, <c>.Optional()</c>, and
    /// <c>.Ignore()</c> calls can be chained to extend or complete coverage.
    /// The builder tracks which destination properties have been accounted for
    /// and throws at build time if any remain unhandled.
    /// </para>
    /// <para>
    /// Assigning the builder to a <see cref="Mapper{TSource,TDest}"/> property
    /// triggers <see cref="MapperBuilder{TSource,TDest}.Build"/> implicitly via
    /// the conversion operator.
    /// </para>
    /// </summary>
    protected MapperBuilder<TSource, TDest> CreateMapper<TSource, TDest>(
        Expression<Func<TSource, TDest>>? memberInitExpression = null)
        => new(memberInitExpression, DefaultCoverageValidation);

    /// <summary>
    /// Creates an enum mapper from a plain mapping method.
    /// The library enumerates every <typeparamref name="TSource"/> value,
    /// calls <paramref name="mappingMethod"/> for each, and builds a
    /// LINQ-translatable conditional expression tree from the results.
    /// </summary>
    protected static EnumMapper<TSource, TDest> CreateEnumMapper<TSource, TDest>(
        Func<TSource, TDest> mappingMethod)
        where TSource : struct, Enum
        where TDest   : struct, Enum
        => new(mappingMethod);

    /// <summary>
    /// Creates a typed <see cref="Variable{T}"/> that can be used inside
    /// mapping expressions as a placeholder value. Declare the returned
    /// instance as a public property on the context so callers can reference
    /// it by name — no magic strings, full IDE navigation.
    /// </summary>
    protected static Variable<T> CreateVariable<T>(string? name = null, T? defaultValue = default)
        => new(name, defaultValue);

    // -------------------------------------------------------------------------
    // Eager build
    // -------------------------------------------------------------------------

    /// <summary>
    /// Forces expression assembly and delegate compilation for every
    /// <see cref="Mapper{TSource,TDest}"/> and <see cref="EnumMapper{TSource,TDest}"/>
    /// declared on this context — both properties and public parameterless methods
    /// that return a mapper type.
    /// Call this at the end of a subclass constructor to surface mapping errors
    /// at startup and eliminate cold-start latency on first use.
    /// </summary>
    protected void EagerBuildAll()
    {
        Type mapperGenericType     = typeof(Mapper<,>);
        Type enumMapperGenericType = typeof(EnumMapper<,>);

        foreach (PropertyInfo property in GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (TryGetMapperFromType(property.PropertyType, mapperGenericType, enumMapperGenericType) &&
                property.GetValue(this) is IMapper mapper)
            {
                mapper.Compile();
            }
        }

        foreach (MethodInfo method in GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (method.GetParameters().Length == 0 &&
                !method.IsSpecialName &&
                TryGetMapperFromType(method.ReturnType, mapperGenericType, enumMapperGenericType) &&
                method.Invoke(this, null) is IMapper mapper)
            {
                mapper.Compile();
            }
        }
    }

    private static bool TryGetMapperFromType(Type type, Type mapperGenericType, Type enumMapperGenericType)
    {
        if (!type.IsGenericType)
        {
            return false;
        }

        Type genericDef = type.GetGenericTypeDefinition();
        return genericDef == mapperGenericType || genericDef == enumMapperGenericType;
    }
}
