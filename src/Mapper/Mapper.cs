using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
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
/// For dictionaries, use <c>ToDictionary</c> with an inline construction
/// expression (nested mapper inlining is not supported inside
/// <c>ToDictionary</c> lambdas):
/// </para>
/// <code>
///     LookupById = src.Items.ToDictionary(i => i.Id, i => new ItemDto { Name = i.Name }),
/// </code>
/// </summary>
public sealed class Mapper<TSource, TDest> : IMapper
{
    private readonly IReadOnlyList<PropertyMapping> _allMappings;
    private readonly Lazy<Func<TSource?, TDest?>>   _compiledDefault;

    private static readonly MethodInfo EnumerableSelectMethod =
        typeof(Enumerable)
            .GetMethods()
            .First(m => m.Name == "Select" && m.GetParameters().Length == 2);

    internal Mapper(IReadOnlyList<PropertyMapping> allMappings)
    {
        _allMappings = allMappings;
        _compiledDefault = new(() =>
            BuildExpression(IncludeSet.All, new Dictionary<object, object?>(), compileSelectors: true)
                .Compile()!);
    }

    // -------------------------------------------------------------------------
    // Core expression builder
    // -------------------------------------------------------------------------

    /// <summary>
    /// Assembles a mapping expression from the stored property list.
    /// For each mapping:
    ///   1. Skip optional properties not present in <paramref name="includes"/>.
    ///   2. If backed by a nested mapper, call <see cref="IMapper.GetExpression"/>
    ///      with the cascaded <see cref="IncludeSet"/> and stitch the result in
    ///      (scalar or collection).
    ///   3. Otherwise, substitute the source parameter and replace variables.
    ///   4. Wrap optional properties with null-safe navigation when compiling.
    /// </summary>
    private Expression<Func<TSource, TDest>> BuildExpression(
        IncludeSet                              includes,
        IReadOnlyDictionary<object, object?>    variableBindings,
        bool                                    compileSelectors = false)
    {
        ParameterExpression sourceParam = Expression.Parameter(typeof(TSource), "src");
        var replacer    = new VariableReplacer(new Dictionary<object, object?>(variableBindings));

        var bindings = new List<MemberBinding>();
        foreach (PropertyMapping mapping in _allMappings)
        {
            if (mapping.Kind == MappingKind.Optional
                && !includes.IncludeAll
                && !includes.Names.Contains(mapping.Destination.Name))
            {
                continue;
            }

            Expression body;

            if (mapping.NestedMapperAccessor != null)
            {
                // Resolve cascaded includes for this nested mapper.
                IncludeSet nestedIncludes = includes.IncludeAll
                    ? IncludeSet.All
                    : includes.Nested.GetValueOrDefault(mapping.Destination.Name, IncludeSet.Empty);

                // Ask the nested mapper for its expression with the cascaded includes.
                IMapper nestedMapper = mapping.NestedMapperAccessor();
                LambdaExpression nestedLambda = nestedMapper.GetExpression(nestedIncludes, variableBindings);

                // Replace the NestedSourceAccess parameter with the current source parameter.
                Expression? accessBody = new ParameterReplacer(mapping.NestedSourceAccess!.Parameters[0], sourceParam)
                                     .Visit(mapping.NestedSourceAccess.Body);
                accessBody = replacer.Visit(accessBody);

                if (!mapping.IsCollection)
                {
                    // Scalar: inline the nested mapper body with source substituted.
                    body = new ParameterReplacer(nestedLambda.Parameters[0], accessBody)
                               .Visit(nestedLambda.Body);

                    // Guard against null source for optional nested mappers.
                    if (mapping.Kind == MappingKind.Optional && !accessBody.Type.IsValueType)
                    {
                        body = Expression.Condition(
                            Expression.Equal(accessBody, Expression.Default(accessBody.Type)),
                            Expression.Default(body.Type),
                            body);
                    }
                }
                else
                {
                    // Collection: build Enumerable.Select(sourceAccess, nestedLambda).ToList() etc.
                    Type srcType  = nestedLambda.Parameters[0].Type;
                    Type destType = nestedLambda.ReturnType;
                    MethodCallExpression selectCall = Expression.Call(
                        EnumerableSelectMethod.MakeGenericMethod(srcType, destType),
                        accessBody,
                        nestedLambda);

                    body = WrapCollection(selectCall, mapping.Destination, destType);
                }
            }
            else
            {
                // Simple mapping: parameter + variable replacement.
                body = new ParameterReplacer(mapping.Source!.Parameters[0], sourceParam)
                           .Visit(mapping.Source.Body);
                body = replacer.Visit(body);
            }

            bindings.Add(Expression.Bind(mapping.Destination, body));
        }

        MemberInitExpression memberInit = Expression.MemberInit(Expression.New(typeof(TDest)), bindings);
        Expression memberInitBody = compileSelectors ? new SelectorCompiler().Visit(memberInit) : memberInit;
        return Expression.Lambda<Func<TSource, TDest>>(memberInitBody, sourceParam);
    }

    /// <summary>
    /// Wraps a <c>Select(...)</c> call with the appropriate materialisation method
    /// (<c>ToList</c>, <c>ToArray</c>, <c>ToHashSet</c>) based on the destination
    /// property type.
    /// </summary>
    private static Expression WrapCollection(Expression selectCall, MemberInfo dest, Type elementType)
    {
        Type? destType = dest switch
        {
            PropertyInfo pi => pi.PropertyType,
            FieldInfo    fi => fi.FieldType,
            _               => null,
        };

        if (destType == null)
        {
            return selectCall;
        }

        string? methodName = null;
        if (destType.IsGenericType)
        {
            Type def = destType.GetGenericTypeDefinition();
            if (def == typeof(List<>))
            {
                methodName = "ToList";
            }
            else if (def == typeof(HashSet<>))
            {
                methodName = "ToHashSet";
            }
            else if (def == typeof(IList<>)
                  || def == typeof(ICollection<>)
                  || def == typeof(IEnumerable<>))
            {
                methodName = null;
            }
        }
        else if (destType.IsArray)
        {
            methodName = "ToArray";
        }

        if (methodName == null)
        {
            return selectCall;
        }

        MethodInfo method = typeof(Enumerable)
            .GetMethod(methodName, BindingFlags.Public | BindingFlags.Static, [typeof(IEnumerable<>).MakeGenericType(elementType)])
            ?? typeof(Enumerable)
               .GetMethods(BindingFlags.Public | BindingFlags.Static)
               .First(m => m.Name == methodName && m.GetParameters().Length == 1)
               .MakeGenericMethod(elementType);

        return Expression.Call(method, selectCall);
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
            return _compiledDefault.Value(source)!;
        }

        var mapOptions = new MapOptions<TDest>();
        options(mapOptions);
        Expression<Func<TSource, TDest>> expr = BuildExpression(IncludeSet.All, mapOptions.VariableBindings, compileSelectors: true);
        return expr.Compile()(source)!;
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

        return _compiledDefault.Value(source)!;
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
        if (options is null)
        {
            return BuildExpression(IncludeSet.Empty, new Dictionary<object, object?>());
        }

        var projOptions = new ProjectionOptions<TDest>();
        options(projOptions);
        var includes = IncludeSet.Parse(projOptions.Includes);
        ValidateIncludes(includes);
        return BuildExpression(includes, projOptions.VariableBindings);
    }

    LambdaExpression IMapper.GetExpression(IncludeSet includes, IReadOnlyDictionary<object, object?> variableBindings)
    {
        return BuildExpression(includes, variableBindings);
    }

    /// <summary>
    /// Replaces every nested <see cref="LambdaExpression"/> node in an expression
    /// tree with a constant holding the pre-compiled delegate so that the outer
    /// compiled delegate does not re-create inner delegate instances on every
    /// invocation.
    /// </summary>
    private sealed class SelectorCompiler : ExpressionVisitor
    {
        protected override Expression VisitLambda<T>(Expression<T> node)
        {
            return Expression.Constant(node.Compile());
        }
    }
}
