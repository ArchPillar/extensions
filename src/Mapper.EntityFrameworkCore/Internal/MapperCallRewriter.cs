using System.Linq.Expressions;
using System.Reflection;
using ArchPillar.Extensions.Mapper.Internal;

namespace ArchPillar.Extensions.Mapper.EntityFrameworkCore.Internal;

/// <summary>
/// Rewrites direct <see cref="Mapper{TSource,TDest}"/> projection calls inside a
/// LINQ query into the mapper's inlined projection expression, so the surrounding
/// (possibly hand-written) query is fully translatable.
/// <list type="bullet">
/// <item><c>mapper.Map(src)</c> / <c>mapper.Map(src, configure)</c> become the mapper's member-init body, parameter-substituted with <c>src</c>.</item>
/// <item><c>src.Project(mapper)</c> / <c>src.Project(mapper, configure)</c> become <c>Enumerable.Select(src, projection)</c>.</item>
/// </list>
/// <para>
/// Shared by two callers with different timing relative to EF Core's funcletizer:
/// <see cref="MapperInliningInterceptor"/> runs <em>after</em> parameter extraction
/// (<paramref name="flattenVariableBoxes"/> = <see langword="true"/>), while the
/// <c>InlineMappers</c> extension runs at query construction, <em>before</em>
/// parameter extraction (<paramref name="flattenVariableBoxes"/> = <see langword="false"/>),
/// so the funcletizer lifts variable and invoke boxes into query parameters normally.
/// </para>
/// </summary>
internal sealed class MapperCallRewriter(bool flattenVariableBoxes) : ExpressionVisitor
{
    private static readonly MethodInfo _enumerableSelectMethod =
        typeof(Enumerable)
            .GetMethods()
            .First(m => m.Name == "Select"
                && m.GetParameters().Length == 2
                && m.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(Func<,>));

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        // mapper.Map(src) — instance call on Mapper<,> (no options)
        if (IsRegularMapCall(node))
        {
            Type[] typeArgs = node.Object!.Type.GetGenericArguments();
            LambdaExpression projection = GetProjection(node.Object!, typeArgs[0], typeArgs[1], configure: null);
            return InlineScalar(projection, Visit(node.Arguments[0])!);
        }

        // mapper.Map(src, configure) — EF options overload
        if (IsRegularMapWithOptionsCall(node))
        {
            Type[] typeArgs = node.Method.GetGenericArguments();
            var configure = MapperConstantEvaluator.Evaluate(node.Arguments[2]);
            LambdaExpression projection = GetProjection(node.Arguments[0], typeArgs[0], typeArgs[1], configure);
            return InlineScalar(projection, Visit(node.Arguments[1])!);
        }

        // src.Project(mapper) — core IEnumerable overload (no options)
        if (IsRegularProjectCall(node))
        {
            Type[] typeArgs = node.Method.GetGenericArguments();
            LambdaExpression projection = GetProjection(node.Arguments[1], typeArgs[0], typeArgs[1], configure: null);
            return InlineCollection(projection, Visit(node.Arguments[0])!, typeArgs[0], typeArgs[1]);
        }

        // src.Project(mapper, configure) — EF options overload
        if (IsRegularProjectWithOptionsCall(node))
        {
            Type[] typeArgs = node.Method.GetGenericArguments();
            var configure = MapperConstantEvaluator.Evaluate(node.Arguments[2]);
            LambdaExpression projection = GetProjection(node.Arguments[1], typeArgs[0], typeArgs[1], configure);
            return InlineCollection(projection, Visit(node.Arguments[0])!, typeArgs[0], typeArgs[1]);
        }

        return base.VisitMethodCall(node);
    }

    /// <summary>
    /// Resolves the mapper instance from <paramref name="mapperAccessor"/> and
    /// returns its projection expression (<c>ToExpression(configure)</c>), with
    /// nested mappers inlined and variables substituted.
    /// <para>
    /// When <c>flattenVariableBoxes</c> is set (the interceptor runs after EF's
    /// funcletizer), <see cref="VariableValueBox{T}"/> accesses are collapsed to
    /// plain constants the relational translator can fold, since the funcletizer
    /// can no longer lift them. In that path a mapper containing an
    /// <see cref="Mapper{TSource,TDest}.Invoke(TSource)"/> cannot be supported —
    /// the invoke box would reach EF's client-projection verifier as a captured
    /// constant — so a clear error directs the caller to <c>InlineMappers()</c>.
    /// </para>
    /// </summary>
    private LambdaExpression GetProjection(
        Expression mapperAccessor, Type sourceType, Type destType, object? configure)
    {
        var mapper = MapperConstantEvaluator.Evaluate(mapperAccessor)!;
        Type mapperType = typeof(Mapper<,>).MakeGenericType(sourceType, destType);
        MethodInfo toExpression = mapperType.GetMethod(nameof(Mapper<int, int>.ToExpression))!;
        var projection = (LambdaExpression)toExpression.Invoke(mapper, [configure])!;

        if (!flattenVariableBoxes)
        {
            return projection;
        }

        if (ContainsInvokeBox(projection))
        {
            throw new InvalidOperationException(
                $"The mapper for '{sourceType.Name}' -> '{destType.Name}' contains an Invoke(...) call, " +
                "which cannot be inlined into a hand-written LINQ query by the EF Core integration " +
                "(the mapper is expanded after EF's parameter extraction). Call .InlineMappers() on the " +
                "query, or project the whole row with .Project(mapper), so the mapper is inlined before " +
                "parameter extraction.");
        }

        return (LambdaExpression)new VariableBoxFlattener().Visit(projection)!;
    }

    private static bool ContainsInvokeBox(Expression expression)
    {
        var finder = new InvokeBoxFinder();
        finder.Visit(expression);
        return finder.Found;
    }

    private sealed class InvokeBoxFinder : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (node.Type.IsGenericType
                && node.Type.GetGenericTypeDefinition() == typeof(MapperInvokeBox<,>))
            {
                Found = true;
            }

            return base.VisitConstant(node);
        }
    }

    private sealed class VariableBoxFlattener : ExpressionVisitor
    {
        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression is ConstantExpression { Value: { } boxed } constant
                && IsVariableValueBox(constant.Type))
            {
                PropertyInfo? property = constant.Type.GetProperty(
                    nameof(VariableValueBox<object>.Value));
                if (property is not null && node.Member == property)
                {
                    return Expression.Constant(property.GetValue(boxed), node.Type);
                }
            }

            return base.VisitMember(node);
        }

        private static bool IsVariableValueBox(Type type)
            => type.IsGenericType
            && type.GetGenericTypeDefinition() == typeof(VariableValueBox<>);
    }

    /// <summary>
    /// Substitutes <paramref name="source"/> for the projection's parameter.
    /// Reference-type sources (other than the never-null query root parameter)
    /// are wrapped in a null guard, matching nested mapper inlining semantics.
    /// </summary>
    private static Expression InlineScalar(LambdaExpression projection, Expression source)
    {
        Expression body = new ParameterReplacer(projection.Parameters[0], source).Visit(projection.Body)!;

        if (!source.Type.IsValueType && source is not ParameterExpression)
        {
            // Compare against a typed null constant rather than a DefaultExpression
            // so EF Core's entity-equality rewrite folds it into an FK IS NULL check
            // instead of attempting EF.Property<TKey>(default(Nav), "Id"), which fails
            // when the navigation's key uses a value converter.
            return Expression.Condition(
                Expression.Equal(source, Expression.Constant(null, source.Type)),
                Expression.Constant(null, body.Type),
                body);
        }

        return body;
    }

    private static Expression InlineCollection(
        LambdaExpression projection, Expression source, Type sourceType, Type destType)
    {
        return Expression.Call(
            _enumerableSelectMethod.MakeGenericMethod(sourceType, destType),
            source,
            projection);
    }

    private static bool IsRegularMapCall(MethodCallExpression node)
        => node.Object != null
        && node.Method.Name == "Map"
        && node.Arguments.Count == 1
        && IsClosedGenericOf(node.Object.Type, typeof(Mapper<,>));

    private static bool IsRegularMapWithOptionsCall(MethodCallExpression node)
        => node.Object == null
        && node.Method.DeclaringType == typeof(MapperEfCoreExtensions)
        && node.Method.Name == "Map";

    private static bool IsRegularProjectCall(MethodCallExpression node)
        => node.Object == null
        && node.Method.DeclaringType == typeof(MapperExtensions)
        && node.Method.Name == "Project"
        && node.Arguments.Count == 2;

    private static bool IsRegularProjectWithOptionsCall(MethodCallExpression node)
        => node.Object == null
        && node.Method.DeclaringType == typeof(MapperEfCoreExtensions)
        && node.Method.Name == "Project";

    private static bool IsClosedGenericOf(Type type, Type genericTypeDefinition)
        => type.IsGenericType && type.GetGenericTypeDefinition() == genericTypeDefinition;
}
