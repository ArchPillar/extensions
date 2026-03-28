using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using ArchPillar.Extensions.Mapper.Internal;

namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// A compiled, reusable mapping between <typeparamref name="TSource"/> and
/// <typeparamref name="TDest"/>. Instances are created via
/// <see cref="MapperContext.CreateMapper{TSource,TDest}"/> and should be held
/// as properties on a <see cref="MapperContext"/> subclass.
/// <para>
/// The same configuration drives both in-memory object mapping (compiled
/// delegate) and LINQ expression projection (expression tree).
/// </para>
/// <para>
/// <b>Nesting inside parent mapper expressions:</b>
/// </para>
/// <para>
/// For a single nested object, call <see cref="Map(TSource)"/> (the
/// expression-safe, no-options overload) — the expression visitor detects
/// the call and inlines this mapper's expression tree into the parent:
/// </para>
/// <code>
/// Order = CreateMapper&lt;Order, OrderDto&gt;(src => new OrderDto
/// {
///     Customer = CustomerMapper.Map(src.Customer),
/// });
/// </code>
/// <para>
/// For nested collections, use the <c>IEnumerable&lt;T&gt;.Project(mapper)</c>
/// extension — also detected and inlined by the visitor:
/// </para>
/// <code>
/// Order = CreateMapper&lt;Order, OrderDto&gt;(src => new OrderDto
/// {
///     Lines = src.Lines.Project(OrderLine).ToList(),
///     Tags  = src.Tags.Project(TagMapper).ToHashSet(),
/// });
/// </code>
/// <para>
/// For dictionaries, use <c>ToDictionary</c> with an inline mapper call —
/// nested mapper inlining works inside <c>ToDictionary</c> value selectors:
/// </para>
/// <code>
///     LookupById = src.Items.ToDictionary(i => i.Id, i => ItemMapper.Map(i)),
/// </code>
/// <para>
/// Nested mappers can also be obtained via method calls (no arguments or constant
/// arguments only) — the expression visitor detects and inlines them automatically:
/// </para>
/// <code>
///     Customer = GetCustomerMapper().Map(src.Customer),
///     Product  = GetMapper&lt;Product, ProductDto&gt;("product").Map(src.Product),
/// </code>
/// </summary>
public sealed class Mapper<TSource, TDest> : IMapper
{
    private readonly IReadOnlyList<IExpressionTransformer>                       _transformers;
    private readonly Lazy<Func<TSource?, List<(object, object?)>?, TDest?>>  _compiled;
    private readonly Lazy<Action<TSource, TDest, List<(object, object?)>?>>  _compiledMapTo;

    /// <summary>
    /// Exposes the property mappings so that
    /// <see cref="MapperContext.Inherit{TSource,TBase}"/> can copy them into
    /// an inherited builder targeting a derived destination type.
    /// </summary>
    internal IReadOnlyList<PropertyMapping> Mappings { get; }

    internal Mapper(
        IReadOnlyList<PropertyMapping> allMappings,
        IReadOnlyList<IExpressionTransformer>? transformers = null)
    {
        Mappings = allMappings;
        _transformers = transformers ?? [];
        _compiled = new(() =>
        {
            return BuildMapExpression().Compile()!;
        });
        _compiledMapTo = new(BuildMapToAction);
    }

    // -------------------------------------------------------------------------
    // Core expression builder
    // -------------------------------------------------------------------------

    /// <summary>
    /// Assembles a raw mapping expression from the stored property list.
    /// For each mapping:
    ///   1. Skip optional properties not present in <paramref name="includes"/>.
    ///   2. Substitute the source parameter.
    ///   3. Run <see cref="NestedMapperInliner"/> to replace every
    ///      <c>Map()</c> / <c>Project()</c> call in-place with the nested
    ///      mapper's inlined body, cascading <paramref name="includes"/> into
    ///      child mappers for the corresponding destination property.
    /// Variable nodes remain as <c>Convert(Variable&lt;T&gt;)</c> in the returned
    /// expression so callers can apply <see cref="VariableReplacer"/> or
    /// <see cref="VariableDictReplacer"/> in a single post-build pass.
    /// </summary>
    private Expression<Func<TSource, TDest>> BuildExpression(
        IncludeSet includes, int depth = 0, bool guardNullOptionalCollections = false)
    {
        if (depth > NestedMapperInliner.MaxNestingDepth)
        {
            throw new InvalidOperationException(
                $"Maximum mapper nesting depth ({NestedMapperInliner.MaxNestingDepth}) exceeded. " +
                "This usually indicates a circular mapper reference.");
        }

        ValidateIncludes(includes);

        ParameterExpression sourceParam = Expression.Parameter(typeof(TSource), "src");

        var bindings = new List<MemberBinding>();
        foreach (PropertyMapping mapping in Mappings)
        {
            if (mapping.Kind == MappingKind.Optional
                && !includes.IncludeAll
                && !includes.Names.Contains(mapping.Destination.Name))
            {
                continue;
            }

            Expression body = new ParameterReplacer(mapping.Source!.Parameters[0], sourceParam)
                                  .Visit(mapping.Source.Body)!;

            IncludeSet nestedIncludes = includes.IncludeAll
                ? IncludeSet.All
                : includes.Nested.GetValueOrDefault(mapping.Destination.Name, IncludeSet.Empty);

            body = new NestedMapperInliner(nestedIncludes, depth).Visit(body)!;

            if (guardNullOptionalCollections && mapping.Kind == MappingKind.Optional)
            {
                body = GuardNullCollectionSource(body);
            }

            bindings.Add(Expression.Bind(mapping.Destination, body));
        }

        var expression = Expression.Lambda<Func<TSource, TDest>>(
            Expression.MemberInit(Expression.New(typeof(TDest)), bindings), sourceParam);

        // Run custom expression transformers: global → per-context → per-mapper
        foreach (IExpressionTransformer transformer in _transformers)
        {
            Expression result = transformer.Transform(expression);
            var transformerName = transformer.GetType().Name;

            if (result is not Expression<Func<TSource, TDest>> typed)
            {
                throw new InvalidOperationException(
                    $"Expression transformer '{transformerName}' returned " +
                    $"{(result is null ? "null" : $"an expression of type '{result.GetType().Name}'")} " +
                    $"instead of Expression<Func<{typeof(TSource).Name}, {typeof(TDest).Name}>>. " +
                    "Transformers must return an expression of the same type as their input.");
            }

            if (typed.Body is not MemberInitExpression)
            {
                throw new InvalidOperationException(
                    $"Expression transformer '{transformerName}' returned a lambda whose body is " +
                    $"'{typed.Body.NodeType}' instead of 'MemberInit'. " +
                    "Transformers must preserve the MemberInit structure of the expression body.");
            }

            expression = typed;
        }

        return expression;
    }

    // -------------------------------------------------------------------------
    // Include validation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Validates that every name in the <see cref="IncludeSet"/> corresponds to
    /// a known mapping. Throws <see cref="InvalidOperationException"/> for
    /// unrecognised names (typically caused by typos in string-path includes).
    /// </summary>
    private void ValidateIncludes(IncludeSet includes)
    {
        foreach (var name in includes.Names)
        {
            if (!Mappings.Any(m => m.Destination.Name == name))
            {
                throw new InvalidOperationException($"Unknown optional property: '{name}'");
            }
        }

        foreach (var name in includes.Nested.Keys)
        {
            if (!Mappings.Any(m => m.Destination.Name == name))
            {
                throw new InvalidOperationException($"Unknown optional property: '{name}'");
            }
        }
    }

    // -------------------------------------------------------------------------
    // Null-guard for optional collection sources (in-memory only)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Wraps an optional mapping body with a null guard when it contains a
    /// LINQ collection chain (e.g. <c>Select(...).ToList()</c>). Walks the
    /// <see cref="Enumerable"/> method call chain to find the root collection
    /// source; if the source is a nullable reference type, the body is wrapped
    /// with <c>source == null ? default : body</c>.
    /// <para>
    /// This prevents <see cref="ArgumentNullException"/> from
    /// <c>Enumerable.Select</c> when an optional collection source is null
    /// during in-memory mapping. Expression projections (EF Core) do not use
    /// this guard — collection navigations are never null in the database.
    /// </para>
    /// </summary>
    private static Expression GuardNullCollectionSource(Expression body)
    {
        if (ExtractCollectionSource(body) is not { Type.IsValueType: false } source)
        {
            return body;
        }

        return Expression.Condition(
            Expression.Equal(source, Expression.Default(source.Type)),
            Expression.Default(body.Type),
            body);
    }

    /// <summary>
    /// Walks an <see cref="Enumerable"/> method call chain
    /// (<c>ToList</c>, <c>ToArray</c>, <c>ToHashSet</c>, <c>Select</c>,
    /// <c>Where</c>, <c>ToDictionary</c>, etc.) and returns the innermost
    /// non-<see cref="Enumerable"/> source expression, or <see langword="null"/>
    /// if the expression is not a recognisable LINQ chain.
    /// </summary>
    private static Expression? ExtractCollectionSource(Expression expression)
    {
        if (expression is not MethodCallExpression call
            || call.Method.DeclaringType != typeof(Enumerable)
            || call.Arguments.Count < 1)
        {
            return null;
        }

        Expression source = call.Arguments[0];

        return ExtractCollectionSource(source) ?? source;
    }

    // -------------------------------------------------------------------------
    // In-memory object mapping
    // -------------------------------------------------------------------------

    /// <summary>
    /// Applies <see cref="VariableDictReplacer"/> and <see cref="SelectorCompiler"/>
    /// as post-build passes over the raw expression, then wraps the result as a
    /// two-parameter <c>(src, vars)</c> delegate. A single compiled instance handles
    /// both the no-variables case (pass <see langword="null"/>) and any variable
    /// binding without recompilation.
    /// </summary>
    private Expression<Func<TSource, List<(object, object?)>?, TDest>> BuildMapExpression()
    {
        ParameterExpression bindingsParam = Expression.Parameter(VariableDictReplacer.BindingsType, "vars");
        Expression<Func<TSource, TDest>> raw = BuildExpression(
            IncludeSet.All, guardNullOptionalCollections: true);
        Expression withLookups = new VariableDictReplacer(bindingsParam).Visit(raw)!;
        Expression body = new SelectorCompiler(bindingsParam).Visit(((LambdaExpression)withLookups).Body);
        return Expression.Lambda<Func<TSource, List<(object, object?)>?, TDest>>(
            body, raw.Parameters[0], bindingsParam);
    }

    private static readonly MethodInfo ReplaceContentsMethod =
        typeof(CollectionUpdater).GetMethod(
            nameof(CollectionUpdater.ReplaceContents),
            BindingFlags.Static | BindingFlags.Public)!;

    private Action<TSource, TDest, List<(object, object?)>?> BuildMapToAction()
    {
        Expression<Func<TSource, List<(object, object?)>?, TDest>> initExpr = BuildMapExpression();
        ParameterExpression bindingsParam = initExpr.Parameters[1];

        var memberInit = (MemberInitExpression)initExpr.Body;
        ParameterExpression destParam = Expression.Parameter(typeof(TDest), "dest");
        ParameterExpression srcParam  = initExpr.Parameters[0];

        var assignments = new List<Expression>(memberInit.Bindings.Count);
        foreach (MemberBinding binding in memberInit.Bindings)
        {
            if (binding is not MemberAssignment assignment)
            {
                throw new InvalidOperationException(
                    $"MapTo requires MemberAssignment bindings but found {binding.BindingType} for '{binding.Member.Name}'.");
            }

            MemberExpression destAccess = Expression.MakeMemberAccess(destParam, assignment.Member);
            Type? elementType = CollectionUpdater.GetCollectionElementType(destAccess.Type);

            if (elementType is not null)
            {
                assignments.Add(BuildCollectionUpdate(destAccess, assignment.Expression, elementType));
            }
            else
            {
                assignments.Add(Expression.Assign(destAccess, assignment.Expression));
            }
        }

        BlockExpression block = Expression.Block(typeof(void), assignments);
        return Expression.Lambda<Action<TSource, TDest, List<(object, object?)>?>>(
            block, srcParam, destParam, bindingsParam).Compile();
    }

    /// <summary>
    /// Builds an expression that updates a collection property in-place:
    /// if the destination collection is non-null, clears and re-populates it
    /// via <see cref="CollectionUpdater.ReplaceContents{T}"/>; otherwise
    /// falls back to direct assignment.
    /// </summary>
    private static ConditionalExpression BuildCollectionUpdate(
        MemberExpression destAccess, Expression sourceExpression, Type elementType)
    {
        Type collectionType = typeof(ICollection<>).MakeGenericType(elementType);
        Type enumerableType = typeof(IEnumerable<>).MakeGenericType(elementType);
        MethodInfo replaceMethod = ReplaceContentsMethod.MakeGenericMethod(elementType);

        Expression replaceCall = Expression.Call(
            replaceMethod,
            Expression.Convert(destAccess, collectionType),
            Expression.Convert(sourceExpression, enumerableType));

        return Expression.IfThenElse(
            Expression.NotEqual(destAccess, Expression.Default(destAccess.Type)),
            replaceCall,
            Expression.Assign(destAccess, sourceExpression));
    }

    /// <summary>
    /// Assigns all mapped properties from <paramref name="source"/> onto an
    /// existing <paramref name="destination"/> instance.
    /// <para>
    /// If <paramref name="source"/> is <see langword="null"/> the call is a
    /// no-op and <paramref name="destination"/> is left unchanged.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="destination"/> is <see langword="null"/>.
    /// </exception>
    public void MapTo(TSource? source, TDest destination, Action<MapOptions<TDest>>? options = null)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (source is null)
        {
            return;
        }

        if (options is null)
        {
            _compiledMapTo.Value(source, destination, null);
            return;
        }

        var mapOptions = new MapOptions<TDest>();
        options(mapOptions);
        _compiledMapTo.Value(source, destination, mapOptions.VariableBindings);
    }

    /// <summary>
    /// Maps a single source instance to a new destination instance.
    /// Returns <see langword="null"/> when <paramref name="source"/> is
    /// <see langword="null"/>.
    /// <para>
    /// Use this overload at call sites outside expression trees.
    /// </para>
    /// </summary>
    [return: NotNullIfNotNull(nameof(source))]
    public TDest? Map(TSource? source, Action<MapOptions<TDest>>? options = null)
    {
        if (source is null)
        {
            return default;
        }

        if (options is null)
        {
            return _compiled.Value(source, null)!;
        }

        var mapOptions = new MapOptions<TDest>();
        options(mapOptions);
        return _compiled.Value(source, mapOptions.VariableBindings)!;
    }

    /// <summary>
    /// Expression-safe single-item overload: no optional parameters, so it
    /// can appear inside parent mapper member-init expressions and inside
    /// lambdas passed to <c>ToDictionary</c>, <c>Where</c>, etc.
    /// The expression visitor inlines this mapper's expression at build time.
    /// <para>
    /// Accepts a nullable source; returns <see langword="null"/> when
    /// <paramref name="source"/> is <see langword="null"/>.
    /// </para>
    /// </summary>
    [return: NotNullIfNotNull(nameof(source))]
    public TDest? Map(TSource? source)
    {
        if (source is null)
        {
            return default;
        }

        return _compiled.Value(source, null)!;
    }

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
    {
        IncludeSet                           includes         = IncludeSet.Empty;
        IReadOnlyDictionary<object, object?> variableBindings = new Dictionary<object, object?>();

        if (options != null)
        {
            var projOptions = new ProjectionOptions<TDest>();
            options(projOptions);
            includes         = IncludeSet.Parse(projOptions.Includes);
            variableBindings = projOptions.VariableBindings;
        }

        return (Expression<Func<TSource, TDest>>)
            new VariableReplacer(new Dictionary<object, object?>(variableBindings))
                .Visit(BuildExpression(includes))!;
    }

    LambdaExpression IMapper.GetRawExpression(IncludeSet includes, int depth)
    {
        return BuildExpression(includes, depth);
    }

    void IMapper.Compile()
    {
        _ = BuildExpression(IncludeSet.Empty);
    }
}
