using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using ArchPillar.Mapper.Internal;

namespace ArchPillar.Mapper;

/// <summary>
/// A compiled, reusable mapping between <typeparamref name="TSource"/> and
/// <typeparamref name="TDest"/>. Instances are created via
/// <see cref="MapperContext.CreateMapper{TSource,TDest}"/> and should be held
/// as properties on a <see cref="MapperContext"/> subclass.
///
/// The same configuration drives both in-memory object mapping (compiled
/// delegate) and LINQ expression projection (expression tree).
///
/// <b>Nesting inside parent mapper expressions:</b>
///
/// For a single nested object, call <see cref="Map(TSource)"/> (the
/// expression-safe, no-options overload) — the expression visitor detects
/// the call and inline this mapper's expression tree into the parent:
/// <code>
/// Order = CreateMapper&lt;Order, OrderDto&gt;(src => new OrderDto
/// {
///     Customer = CustomerMapper.Map(src.Customer),
/// });
/// </code>
///
/// For nested collections, use the <c>IEnumerable&lt;T&gt;.Project(mapper)</c>
/// extension — also detected and inlined by the visitor:
/// <code>
/// Order = CreateMapper&lt;Order, OrderDto&gt;(src => new OrderDto
/// {
///     Lines = src.Lines.Project(OrderLine).ToList(),
///     Tags  = src.Tags.Project(TagMapper).ToHashSet(),
/// });
/// </code>
///
/// For dictionaries, call <see cref="Map(TSource)"/> inline inside the
/// standard <c>ToDictionary</c> lambda:
/// <code>
///     LookupById = src.Items.ToDictionary(i => i.Id, i => ItemMapper.Map(i)),
/// </code>
/// </summary>
public sealed class Mapper<TSource, TDest> : IMapper
{
    private readonly IReadOnlyList<PropertyMapping>    _allMappings;
    private readonly Lazy<Func<TSource?, TDest?>>      _compiledDefault;

    internal Mapper(IReadOnlyList<PropertyMapping> allMappings)
    {
        _allMappings     = allMappings;
        // Compile mode uses null-safe navigation for optional properties.
        _compiledDefault = new(() => BuildExpression([], new Dictionary<object, object?>(), nullSafeOptionals: true).Compile()!);
    }

    // -------------------------------------------------------------------------
    // Core expression builder
    // -------------------------------------------------------------------------

    /// <summary>
    /// Assembles a fresh mapping expression from the stored property list.
    /// </summary>
    /// <param name="includeNames">Names of optional destination properties to include.</param>
    /// <param name="variableBindings">Variable-to-value bindings; unbound variables resolve to their default.</param>
    /// <param name="nullSafeOptionals">
    /// When <see langword="true"/> (compile / in-memory mode), optional property source expressions
    /// are wrapped with null guards for each intermediate reference-type member access so that a null
    /// leg returns <see langword="default"/> instead of throwing.
    /// When <see langword="false"/> (IQueryable projection mode), source expressions are used as-is
    /// so that LINQ providers can translate them to SQL without interference.
    /// </param>
    private Expression<Func<TSource, TDest>> BuildExpression(
        HashSet<string>                      includeNames,
        IReadOnlyDictionary<object, object?> variableBindings,
        bool                                 nullSafeOptionals)
    {
        var sourceParam = Expression.Parameter(typeof(TSource), "src");
        var replacer    = new VariableReplacer(new Dictionary<object, object?>(variableBindings));

        var bindings = new List<MemberBinding>();
        foreach (var mapping in _allMappings)
        {
            if (mapping.Kind == MappingKind.Optional && !includeNames.Contains(mapping.Destination.Name))
                continue;

            var body = new ParameterReplacer(mapping.Source!.Parameters[0], sourceParam)
                           .Visit(mapping.Source.Body);
            body = replacer.Visit(body);

            if (nullSafeOptionals && mapping.Kind == MappingKind.Optional)
                body = AddNullSafeNavigation(body);

            bindings.Add(Expression.Bind(mapping.Destination, body));
        }

        var memberInit = Expression.MemberInit(Expression.New(typeof(TDest)), bindings);
        return Expression.Lambda<Func<TSource, TDest>>(memberInit, sourceParam);
    }

    /// <summary>
    /// Wraps member-access chains with null guards for every intermediate
    /// reference-type member access, so that a null leg returns <c>default</c>
    /// rather than throwing.
    ///
    /// Example: <c>src.Customer.Name</c> →
    /// <c>src.Customer == null ? null : src.Customer.Name</c>
    /// </summary>
    private static Expression AddNullSafeNavigation(Expression body)
    {
        // Collect the member-access chain, outermost node first.
        var chain = new List<MemberExpression>();
        var node  = body;
        while (node is MemberExpression me)
        {
            chain.Add(me);
            node = me.Expression;
        }

        // Only rewrite chains rooted at the source parameter.
        if (node is not ParameterExpression || chain.Count == 0)
            return body;

        // chain[0]  = the final access (e.g. src.Customer.Name)
        // chain[1+] = intermediate navigations (e.g. src.Customer)
        // Add a conditional null-check for each intermediate reference-type navigation.
        var result = body;
        for (var i = 1; i < chain.Count; i++)
        {
            var intermediate = chain[i];
            if (intermediate.Type.IsValueType && Nullable.GetUnderlyingType(intermediate.Type) == null)
                continue; // non-nullable value type — cannot be null

            result = Expression.Condition(
                Expression.Equal(intermediate, Expression.Default(intermediate.Type)),
                Expression.Default(body.Type),
                result);
        }

        return result;
    }

    // -------------------------------------------------------------------------
    // In-memory object mapping
    // -------------------------------------------------------------------------

    /// <summary>
    /// Maps a single source instance to a new destination instance.
    /// Returns <see langword="null"/> when <paramref name="source"/> is
    /// <see langword="null"/>.
    ///
    /// Use this overload at call sites outside expression trees.
    /// </summary>
    [return: NotNullIfNotNull(nameof(source))]
    public TDest? Map(TSource? source, Action<MapOptions<TDest>>? options = null)
        => throw new NotImplementedException();

    /// <summary>
    /// Expression-safe single-item overload: no optional parameters, so it
    /// can appear inside parent mapper member-init expressions and inside
    /// lambdas passed to <c>ToDictionary</c>, <c>Where</c>, etc.
    /// The expression visitor inlines this mapper's expression at build time.
    ///
    /// Accepts a nullable source; returns <see langword="null"/> when
    /// <paramref name="source"/> is <see langword="null"/>.
    /// </summary>
    [return: NotNullIfNotNull(nameof(source))]
    public TDest? Map(TSource? source)
        => throw new NotImplementedException();

    // -------------------------------------------------------------------------
    // LINQ / expression projection
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the mapping as a composable LINQ expression tree, including
    /// any requested optional properties and variable bindings.
    /// The expression can be passed directly to a LINQ provider (e.g. EF Core).
    /// </summary>
    public Expression<Func<TSource, TDest>> ToExpression(
        Action<ProjectionOptions<TDest>>? options = null)
        => throw new NotImplementedException();

    LambdaExpression IMapper.GetBaseExpression() => ToExpression();
}
