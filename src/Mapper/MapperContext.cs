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
    private readonly IReadOnlyList<IExpressionTransformer> _globalTransformers;
    private readonly List<IExpressionTransformer>          _contextTransformers = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="MapperContext"/> class
    /// without global options.
    /// </summary>
    protected MapperContext()
        : this(null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MapperContext"/> class
    /// with optional global mapper options. Pass a shared
    /// <see cref="GlobalMapperOptions"/> instance (typically registered as a
    /// DI singleton) to apply global transformers to all mappers in this context.
    /// </summary>
    protected MapperContext(GlobalMapperOptions? globalOptions)
    {
        _globalTransformers = globalOptions?.Transformers ?? [];
    }

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
    // Expression transformers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registers a per-context expression transformer that will run on every
    /// mapper expression tree created by this context, after any global
    /// transformers but before per-mapper transformers.
    /// Call this in the subclass constructor before creating mappers.
    /// </summary>
    protected void AddTransformer(IExpressionTransformer transformer)
    {
        ArgumentNullException.ThrowIfNull(transformer);
        _contextTransformers.Add(transformer);
    }

    // -------------------------------------------------------------------------
    // Factory methods
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns an intermediate builder that inherits all property mappings
    /// from an existing <see cref="Mapper{TSource,TBase}"/>. Call
    /// <see cref="InheritedMapperBuilder{TSource,TBase}.For{TDest}"/> on
    /// the result to specify the derived destination type and obtain a
    /// <see cref="MapperBuilder{TSource,TDest}"/> pre-populated with the
    /// base mappings.
    /// <para>
    /// Use this to map the same source type to a type hierarchy:
    /// </para>
    /// <code>
    /// // Base mapping: Order → OrderSummaryDto
    /// OrderSummary = CreateMapper&lt;Order, OrderSummaryDto&gt;(src => new OrderSummaryDto
    /// {
    ///     Id   = src.Id,
    ///     Date = src.CreatedAt,
    /// });
    ///
    /// // Derived mapping: Order → OrderDetailDto (inherits OrderSummaryDto)
    /// OrderDetail = Inherit(OrderSummary).For&lt;OrderDetailDto&gt;()
    ///     .Map(dest => dest.Lines, src => src.Lines.Project(OrderLine).ToList());
    /// </code>
    /// </summary>
    protected InheritedMapperBuilder<TSource, TBase> Inherit<TSource, TBase>(
        Mapper<TSource, TBase> baseMapper)
        => new(baseMapper.Mappings, DefaultCoverageValidation, _globalTransformers, _contextTransformers);

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
        => new(memberInitExpression, DefaultCoverageValidation, _globalTransformers, _contextTransformers);

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
    /// Creates a symmetric (bidirectional) enum mapper from a single forward
    /// mapping method. The reverse mapping is derived automatically by
    /// inverting the computed pairs.
    /// <para>
    /// The mapping must be bijective (one-to-one). If two
    /// <typeparamref name="TLeft"/> values map to the same
    /// <typeparamref name="TRight"/> value, an
    /// <see cref="InvalidOperationException"/> is thrown at build time.
    /// </para>
    /// </summary>
    protected static SymmetricEnumMapper<TLeft, TRight> CreateSymmetricEnumMapper<TLeft, TRight>(
        Func<TLeft, TRight> forwardMethod)
        where TLeft  : struct, Enum
        where TRight : struct, Enum
        => new(forwardMethod);

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
    /// <see cref="Mapper{TSource,TDest}"/>, <see cref="EnumMapper{TSource,TDest}"/>,
    /// and <see cref="SymmetricEnumMapper{TLeft,TRight}"/>
    /// declared on this context — both properties and public parameterless methods
    /// that return a mapper type.
    /// Call this at the end of a subclass constructor to surface mapping errors
    /// at startup and eliminate cold-start latency on first use.
    /// </summary>
    protected void EagerBuildAll()
    {
        Type mapperGenericType              = typeof(Mapper<,>);
        Type enumMapperGenericType          = typeof(EnumMapper<,>);
        Type symmetricEnumMapperGenericType = typeof(SymmetricEnumMapper<,>);

        foreach (PropertyInfo property in GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (TryGetMapperFromType(property.PropertyType, mapperGenericType, enumMapperGenericType, symmetricEnumMapperGenericType) &&
                property.GetValue(this) is IMapper mapper)
            {
                mapper.Compile();
            }
        }

        foreach (MethodInfo method in GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (method.GetParameters().Length == 0 &&
                !method.IsSpecialName &&
                TryGetMapperFromType(method.ReturnType, mapperGenericType, enumMapperGenericType, symmetricEnumMapperGenericType) &&
                method.Invoke(this, null) is IMapper mapper)
            {
                mapper.Compile();
            }
        }
    }

    private static bool TryGetMapperFromType(
        Type type, Type mapperGenericType, Type enumMapperGenericType, Type symmetricEnumMapperGenericType)
    {
        if (!type.IsGenericType)
        {
            return false;
        }

        Type genericDef = type.GetGenericTypeDefinition();
        return genericDef == mapperGenericType
            || genericDef == enumMapperGenericType
            || genericDef == symmetricEnumMapperGenericType;
    }
}
