using System.Linq.Expressions;
using System.Reflection;
using ArchPillar.Extensions.Mapper.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ArchPillar.Extensions.Mapper.EntityFrameworkCore.Internal;

/// <summary>
/// Rewrites direct <see cref="Mapper{TSource,TDest}"/> projection calls that
/// appear inside a LINQ query into the mapper's inlined projection expression,
/// so the surrounding (possibly hand-written) query is fully translatable.
/// <list type="bullet">
/// <item><c>mapper.Map(src)</c> and <c>mapper.Map(src, configure)</c> are replaced with the mapper's member-init body, parameter-substituted with <c>src</c>.</item>
/// <item><c>src.Project(mapper)</c> and <c>src.Project(mapper, configure)</c> are replaced with <c>Enumerable.Select(src, projection)</c>.</item>
/// </list>
/// <para>
/// Enum mappers are handled separately by <see cref="EnumMapperQueryInterceptor"/>
/// (translated to flat SQL CASE); this interceptor only inlines regular
/// <see cref="Mapper{TSource,TDest}"/> instances.
/// </para>
/// </summary>
internal sealed class MapperInliningInterceptor : IQueryExpressionInterceptor
{
    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        return new MapperCallRewriter().Visit(queryExpression);
    }

    private sealed class MapperCallRewriter : ExpressionVisitor
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
        /// <see cref="VariableReplacer"/> emits each variable as a property access
        /// on a captured <see cref="VariableValueBox{T}"/> so EF Core's funcletizer
        /// can lift it into a SQL parameter. In the inlining path, however, the
        /// funcletizer has already run by the time this interceptor fires — any
        /// box constants we introduce post-extraction would be flagged as client
        /// projections. We collapse them here to plain
        /// <see cref="ConstantExpression"/> nodes that the relational translator
        /// can fold into the SQL directly (with provider-side literal escaping).
        /// </para>
        /// </summary>
        private static LambdaExpression GetProjection(
            Expression mapperAccessor, Type sourceType, Type destType, object? configure)
        {
            var mapper = MapperConstantEvaluator.Evaluate(mapperAccessor)!;
            Type mapperType = typeof(Mapper<,>).MakeGenericType(sourceType, destType);
            MethodInfo toExpression = mapperType.GetMethod(nameof(Mapper<int, int>.ToExpression))!;
            var projection = (LambdaExpression)toExpression.Invoke(mapper, [configure])!;
            return (LambdaExpression)new VariableBoxFlattener().Visit(projection)!;
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

    private sealed class ParameterReplacer(ParameterExpression parameter, Expression replacement) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == parameter ? replacement : base.VisitParameter(node);
    }
}
