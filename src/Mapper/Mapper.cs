using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using ArchPillar.Mapper.Internal;

namespace ArchPillar.Mapper;

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
/// </summary>
public sealed class Mapper<TSource, TDest> : IMapper
{
    private readonly IReadOnlyList<PropertyMapping>                              _allMappings;
    private readonly Lazy<Func<TSource?, List<(object, object?)>?, TDest?>>  _compiled;
    private readonly Lazy<Action<TSource, TDest, List<(object, object?)>?>>  _compiledMapTo;

    internal Mapper(IReadOnlyList<PropertyMapping> allMappings)
    {
        _allMappings = allMappings;
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
    private Expression<Func<TSource, TDest>> BuildExpression(IncludeSet includes)
    {
        ValidateIncludes(includes);

        ParameterExpression sourceParam = Expression.Parameter(typeof(TSource), "src");

        var bindings = new List<MemberBinding>();
        foreach (PropertyMapping mapping in _allMappings)
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

            body = new NestedMapperInliner(nestedIncludes).Visit(body)!;

            bindings.Add(Expression.Bind(mapping.Destination, body));
        }

        return Expression.Lambda<Func<TSource, TDest>>(
            Expression.MemberInit(Expression.New(typeof(TDest)), bindings), sourceParam);
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
            if (!_allMappings.Any(m => m.Destination.Name == name))
            {
                throw new InvalidOperationException($"Unknown optional property: '{name}'");
            }
        }

        foreach (var name in includes.Nested.Keys)
        {
            if (!_allMappings.Any(m => m.Destination.Name == name))
            {
                throw new InvalidOperationException($"Unknown optional property: '{name}'");
            }
        }
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
        Expression<Func<TSource, TDest>> raw = BuildExpression(IncludeSet.All);
        Expression withLookups = new VariableDictReplacer(bindingsParam).Visit(raw)!;
        Expression body = new SelectorCompiler(bindingsParam).Visit(((LambdaExpression)withLookups).Body);
        return Expression.Lambda<Func<TSource, List<(object, object?)>?, TDest>>(
            body, raw.Parameters[0], bindingsParam);
    }

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

            assignments.Add(Expression.Assign(
                Expression.MakeMemberAccess(destParam, assignment.Member),
                assignment.Expression));
        }

        BlockExpression block = Expression.Block(typeof(void), assignments);
        return Expression.Lambda<Action<TSource, TDest, List<(object, object?)>?>>(
            block, srcParam, destParam, bindingsParam).Compile();
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

    LambdaExpression IMapper.GetRawExpression(IncludeSet includes)
    {
        return BuildExpression(includes);
    }

    LambdaExpression IMapper.GetExpression(IncludeSet includes, IReadOnlyDictionary<object, object?> variableBindings)
    {
        return (LambdaExpression)
            new VariableReplacer(new Dictionary<object, object?>(variableBindings))
                .Visit(BuildExpression(includes))!;
    }

    /// <summary>
    /// Replaces every nested <see cref="LambdaExpression"/> node in an expression
    /// tree with a constant holding the pre-compiled delegate so that the outer
    /// compiled delegate does not re-create inner delegate instances on every
    /// invocation.
    /// <para>
    /// When <paramref name="dictParam"/> is supplied, lambdas that reference it as a
    /// free variable are left as-is — they will close over it at runtime rather than
    /// being pre-compiled to a stale constant.
    /// </para>
    /// </summary>
    private sealed class SelectorCompiler(ParameterExpression? dictParam = null) : ExpressionVisitor
    {
        protected override Expression VisitLambda<T>(Expression<T> node)
        {
            if (dictParam != null && ReferencesDictParam(node.Body))
            {
                return base.VisitLambda(node);
            }

            return Expression.Constant(node.Compile());
        }

        private bool ReferencesDictParam(Expression expr)
        {
            var finder = new DictParamFinder(dictParam!);
            finder.Visit(expr);
            return finder.Found;
        }

        private sealed class DictParamFinder(ParameterExpression target) : ExpressionVisitor
        {
            public bool Found { get; private set; }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                Found |= node == target;
                return node;
            }
        }
    }
}
