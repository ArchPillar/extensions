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
public sealed class Mapper<TSource, TDest> : IMapper, IInvokeExpressionBuilder
{
    private readonly IReadOnlyList<IExpressionTransformer>                       _transformers;
    private readonly NullHandling                                                _nullHandling;
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
        NullHandling nullHandling = NullHandling.Lenient,
        IReadOnlyList<IExpressionTransformer>? transformers = null)
    {
        Mappings = allMappings;
        _nullHandling = nullHandling;
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
        IncludeSet includes, int depth = 0, bool inMemory = false)
    {
        if (depth > NestedMapperInliner.MaxNestingDepth)
        {
            throw new InvalidOperationException(
                $"Maximum mapper nesting depth ({NestedMapperInliner.MaxNestingDepth}) exceeded. " +
                "This usually indicates a circular mapper reference.");
        }

        ValidateIncludes(includes);

        ParameterExpression sourceParam = Expression.Parameter(typeof(TSource), "src");
        NullabilityInfoContext? nullability = inMemory ? new() : null;

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
                                  .Visit(mapping.Source.Body);

            IncludeSet nestedIncludes = includes.IncludeAll
                ? IncludeSet.All
                : includes.Nested.GetValueOrDefault(mapping.Destination.Name, IncludeSet.Empty);

            body = new NestedMapperInliner(nestedIncludes, depth, inMemory, _nullHandling, Describe())
                .Visit(body);

            if (nullability is not null)
            {
                body = GuardNullCollectionSource(body, mapping.Destination, nullability);
            }

            MemberBinding binding;
            try
            {
                binding = Expression.Bind(mapping.Destination, body);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException(
                    $"Mapper '{TypeDisplay.Describe(typeof(TSource))}' -> '{TypeDisplay.Describe(typeof(TDest))}': the mapping for " +
                    $"destination property '{mapping.Destination.Name}' (of type '{TypeDisplay.Describe(TypeDisplay.MemberType(mapping.Destination))}') " +
                    $"produced a value of type '{TypeDisplay.Describe(body.Type)}', which cannot be assigned to it. " +
                    "Check this property's mapping — the mapped source value's type does not match the destination " +
                    "property's type (this commonly happens when a nested mapper returns a different type than the " +
                    "property expects).",
                    ex);
            }

            bindings.Add(binding);
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
    // Null handling for collection sources (in-memory only)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Applies the mapper's <see cref="NullHandling"/> to a mapping body that is a LINQ
    /// collection chain (e.g. <c>src.Lines.Select(...).ToList()</c>) whose root source is
    /// a reference type:
    /// <list type="bullet">
    /// <item><see cref="NullHandling.Strict"/>, and either the source or the destination
    /// is declared non-nullable: a <see langword="null"/> source fails with a message
    /// naming both.</item>
    /// <item>Destination declared non-nullable: the root source is coalesced with an empty
    /// sequence, so the chain yields an empty collection of the destination's type.</item>
    /// <item>Otherwise: <c>source == null ? null : body</c>.</item>
    /// </list>
    /// Expression projections (EF Core) never use this guard: collection navigations are
    /// never <see langword="null"/> in the database.
    /// </summary>
    private Expression GuardNullCollectionSource(
        Expression body, MemberInfo destination, NullabilityInfoContext nullability)
    {
        if (FindInnermostCollectionCall(body) is not { } innermost
            || innermost.Arguments[0] is not { Type.IsValueType: false } source)
        {
            return body;
        }

        NullabilityState sourceState      = NullSource.ReadState(source, nullability);
        NullabilityState destinationState = NullSource.WriteState(destination, nullability);
        Expression isNull = Expression.Equal(source, Expression.Constant(null, source.Type));

        if (_nullHandling == NullHandling.Strict
            && (sourceState == NullabilityState.NotNull || destinationState == NullabilityState.NotNull))
        {
            var declared = sourceState == NullabilityState.NotNull
                ? NullSource.Describe(source)
                : $"destination property '{destination.Name}'";
            var message =
                $"{Describe()}: the source collection {NullSource.Describe(source)} for destination property " +
                $"'{destination.Name}' is null, but {declared} is declared non-nullable. Load or initialise the " +
                "collection, or declare both sides nullable. This mapper uses NullHandling.Strict; with " +
                "NullHandling.Lenient it would map to an empty collection or null.";
            return Expression.Condition(isNull, NullSource.BuildFail(body.Type, message), body);
        }

        Type sequenceType = innermost.Method.GetParameters()[0].ParameterType;
        Type elementType  = sequenceType.IsGenericType ? sequenceType.GetGenericArguments()[0] : typeof(object);
        MethodCallExpression empty = Expression.Call(typeof(Enumerable), nameof(Enumerable.Empty), [elementType]);

        if (destinationState == NullabilityState.NotNull && sequenceType.IsAssignableFrom(empty.Type))
        {
            MethodCallExpression coalesced = innermost.Update(
                innermost.Object,
                [Expression.Coalesce(Expression.Convert(source, sequenceType), empty), .. innermost.Arguments.Skip(1)]);
            return new NodeReplacer(innermost, coalesced).Visit(body)!;
        }

        return Expression.Condition(isNull, Expression.Default(body.Type), body);
    }

    /// <summary>
    /// Walks an <see cref="Enumerable"/> method call chain (<c>ToList</c>, <c>ToArray</c>,
    /// <c>ToHashSet</c>, <c>Select</c>, <c>Where</c>, <c>ToDictionary</c>, etc.) and returns
    /// the innermost call, whose first argument is the chain's root source, or
    /// <see langword="null"/> if the expression is not a recognisable LINQ chain.
    /// </summary>
    private static MethodCallExpression? FindInnermostCollectionCall(Expression expression)
    {
        if (expression is not MethodCallExpression call
            || call.Method.DeclaringType != typeof(Enumerable)
            || call.Arguments.Count < 1)
        {
            return null;
        }

        return FindInnermostCollectionCall(call.Arguments[0]) ?? call;
    }

    private static string Describe()
        => $"Mapper '{TypeDisplay.Describe(typeof(TSource))}' -> '{TypeDisplay.Describe(typeof(TDest))}'";

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
            IncludeSet.All, inMemory: true);
        Expression withLookups = new VariableDictReplacer(bindingsParam).Visit(raw);
        Expression body = new SelectorCompiler(bindingsParam).Visit(((LambdaExpression)withLookups).Body);
        return Expression.Lambda<Func<TSource, List<(object, object?)>?, TDest>>(
            body, raw.Parameters[0], bindingsParam);
    }

    private static readonly MethodInfo _replaceContentsMethod =
        typeof(CollectionUpdater).GetMethod(
            nameof(CollectionUpdater.ReplaceContents),
            BindingFlags.Static | BindingFlags.Public)!;

    private static readonly MethodInfo _mergeWithIdentityMethod =
        typeof(CollectionUpdater).GetMethod(
            nameof(CollectionUpdater.MergeWithIdentity),
            BindingFlags.Static | BindingFlags.Public)!;

    private Action<TSource, TDest, List<(object, object?)>?> BuildMapToAction()
    {
        Expression<Func<TSource, List<(object, object?)>?, TDest>> initExpr = BuildMapExpression();
        ParameterExpression bindingsParam = initExpr.Parameters[1];

        var memberInit = (MemberInitExpression)initExpr.Body;
        ParameterExpression destParam = Expression.Parameter(typeof(TDest), "dest");
        ParameterExpression srcParam  = initExpr.Parameters[0];

        // Index mappings by destination name for collection config lookup
        var mappingsByDest = Mappings.ToDictionary(m => m.Destination.Name, StringComparer.Ordinal);

        var assignments = new List<Expression>(memberInit.Bindings.Count);
        foreach (MemberBinding binding in memberInit.Bindings)
        {
            if (binding is not MemberAssignment assignment)
            {
                throw new InvalidOperationException(
                    $"MapTo requires MemberAssignment bindings but found {binding.BindingType} for '{binding.Member.Name}'.");
            }

            MemberExpression destAccess = Expression.MakeMemberAccess(destParam, assignment.Member);

            CollectionMapToConfig? config = mappingsByDest.GetValueOrDefault(binding.Member.Name)?.CollectionConfig;
            CollectionMapToMode mode = config?.Mode ?? CollectionMapToMode.Shallow;

            switch (mode)
            {
                case CollectionMapToMode.Deep:
                {
                    Type? elementType = CollectionUpdater.GetCollectionElementType(destAccess.Type);
                    if (elementType is not null)
                    {
                        assignments.Add(BuildDeepCollectionUpdate(destAccess, assignment.Expression, elementType));
                    }
                    else
                    {
                        assignments.Add(Expression.Assign(destAccess, assignment.Expression));
                    }

                    break;
                }

                case CollectionMapToMode.DeepWithIdentity:
                {
                    assignments.Add(BuildIdentityMerge(destAccess, srcParam, config!));
                    break;
                }

                default:
                {
                    assignments.Add(Expression.Assign(destAccess, assignment.Expression));
                    break;
                }
            }
        }

        BlockExpression block = Expression.Block(typeof(void), assignments);
        return Expression.Lambda<Action<TSource, TDest, List<(object, object?)>?>>(
            block, srcParam, destParam, bindingsParam).Compile();
    }

    /// <summary>
    /// <see cref="CollectionMapToMode.Deep"/>: preserves the destination
    /// collection instance by clearing and re-adding mapped items. Falls back
    /// to assignment when the destination collection is <see langword="null"/>.
    /// The source expression has already had the mapper's <see cref="NullHandling"/>
    /// applied, so a <see langword="null"/> source collection arrives here as an empty
    /// collection or <see langword="null"/>, both of which clear the destination.
    /// </summary>
    private static ConditionalExpression BuildDeepCollectionUpdate(
        MemberExpression destAccess, Expression sourceExpression, Type elementType)
    {
        Type collectionType = typeof(ICollection<>).MakeGenericType(elementType);
        Type enumerableType = typeof(IEnumerable<>).MakeGenericType(elementType);
        MethodInfo replaceMethod = _replaceContentsMethod.MakeGenericMethod(elementType);

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
    /// <see cref="CollectionMapToMode.DeepWithIdentity"/>: generates a call to
    /// <see cref="CollectionUpdater.MergeWithIdentity{TSourceItem,TDestItem,TKey}"/>
    /// that matches source/destination elements by key, uses <c>MapTo</c> for
    /// existing items, <c>Map</c> for new items, and removes unmatched items.
    /// Skips the merge (no-op) when the destination collection is
    /// <see langword="null"/>.
    /// </summary>
    private Expression BuildIdentityMerge(
        MemberExpression destAccess,
        ParameterExpression srcParam,
        CollectionMapToConfig config)
    {
        // Extract generic type arguments from the pre-built delegates
        Type[] sourceKeyArgs = config.SourceKeySelector!.GetType().GetGenericArguments();
        Type sourceItemType  = sourceKeyArgs[0];
        Type keyType         = sourceKeyArgs[^1];
        Type[] destKeyArgs   = config.DestKeySelector!.GetType().GetGenericArguments();
        Type destItemType    = destKeyArgs[0];

        // Substitute the source parameter into the source collection accessor
        LambdaExpression sourceAccessor = config.SourceCollectionAccessor!;
        Expression sourceCollection = new ParameterReplacer(sourceAccessor.Parameters[0], srcParam)
            .Visit(sourceAccessor.Body);

        if (_nullHandling == NullHandling.Strict
            && !sourceCollection.Type.IsValueType
            && NullSource.ReadState(sourceCollection, new NullabilityInfoContext()) == NullabilityState.NotNull)
        {
            var message =
                $"{Describe()}: the source collection {NullSource.Describe(sourceCollection)} for destination property " +
                $"'{destAccess.Member.Name}' is null, but it is declared non-nullable. Load or initialise the collection, " +
                "or declare it nullable. This mapper uses NullHandling.Strict; with NullHandling.Lenient it would " +
                "clear the destination collection.";
            sourceCollection = Expression.Condition(
                Expression.Equal(sourceCollection, Expression.Constant(null, sourceCollection.Type)),
                NullSource.BuildFail(sourceCollection.Type, message),
                sourceCollection);
        }

        MethodInfo mergeMethod = _mergeWithIdentityMethod.MakeGenericMethod(sourceItemType, destItemType, keyType);
        Type collectionType = typeof(ICollection<>).MakeGenericType(destItemType);
        Type enumerableType = typeof(IEnumerable<>).MakeGenericType(sourceItemType);

        Expression mergeCall = Expression.Call(
            mergeMethod,
            Expression.Convert(destAccess, collectionType),
            Expression.Convert(sourceCollection, enumerableType),
            Expression.Constant(config.SourceKeySelector),
            Expression.Constant(config.DestKeySelector),
            Expression.Constant(config.MapFunc),
            Expression.Constant(config.MapToAction));

        // When dest collection is null, we can't merge — skip (no-op)
        return Expression.IfThen(
            Expression.NotEqual(destAccess, Expression.Default(destAccess.Type)),
            mergeCall);
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

    /// <summary>
    /// Maps a single item by <em>invoking</em> this mapper as a function rather
    /// than inlining its expression tree. Inside a LINQ projection the nested
    /// mapper is not folded into the query; instead it is compiled to a delegate
    /// call that the provider evaluates on the materialised source. Use it when
    /// the nested mapping cannot (or should not) be translated by the LINQ
    /// provider — for example when it routes through a custom instance method the
    /// provider would reject.
    /// <para>
    /// Works on the <c>Project</c> / <c>ToExpression</c> projection path, with or
    /// without the EF Core integration (<c>UseArchPillarMapper</c>). Behaves
    /// identically to <see cref="Map(TSource)"/> for in-memory mapping; returns
    /// <see langword="null"/> when <paramref name="source"/> is
    /// <see langword="null"/>.
    /// </para>
    /// </summary>
    [return: NotNullIfNotNull(nameof(source))]
    public TDest? Invoke(TSource? source) => Map(source);

    // The MapperInvokeBox.Map member, resolved once from a compiler-built
    // expression (an ldtoken reference) rather than a string-based reflection
    // lookup, so the member survives trimming and the code is AOT-safe.
    private static readonly MemberInfo _invokeBoxMapMember = ResolveInvokeBoxMapMember();

    private static MemberInfo ResolveInvokeBoxMapMember()
    {
        Expression<Func<MapperInvokeBox<TSource, TDest?>, Func<TSource, TDest?>>> accessor = box => box.Map;
        return ((MemberExpression)accessor.Body).Member;
    }

    /// <summary>
    /// Builds <c>box.Map.Invoke(source)</c>, where <c>box</c> carries this
    /// mapper's cached <see cref="Map(TSource)"/> delegate in a non-mapper
    /// <see cref="MapperInvokeBox{TSource,TResult}"/>. No reflection or generic
    /// type construction is needed — the closed generic types are known here, so
    /// a provider's funcletizer parameterizes <c>box.Map</c> and runs the mapping
    /// in memory on the materialised source.
    /// </summary>
    Expression IInvokeExpressionBuilder.BuildInvokeExpression(Expression source)
    {
        var box = new MapperInvokeBox<TSource, TDest?>(Map);
        Expression mapAccess = Expression.MakeMemberAccess(Expression.Constant(box), _invokeBoxMapMember);
        return Expression.Invoke(mapAccess, source);
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
                .Visit(BuildExpression(includes));
    }

    LambdaExpression IMapper.GetRawExpression(
        IncludeSet includes, int depth, bool inMemory)
    {
        return BuildExpression(includes, depth, inMemory);
    }

    void IMapper.Compile()
    {
        _ = BuildExpression(IncludeSet.Empty);
    }
}
