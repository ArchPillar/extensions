using System.Linq.Expressions;
using System.Reflection;
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
                var configure = Evaluate(node.Arguments[2]);
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
                var configure = Evaluate(node.Arguments[2]);
                LambdaExpression projection = GetProjection(node.Arguments[1], typeArgs[0], typeArgs[1], configure);
                return InlineCollection(projection, Visit(node.Arguments[0])!, typeArgs[0], typeArgs[1]);
            }

            return base.VisitMethodCall(node);
        }

        /// <summary>
        /// Resolves the mapper instance from <paramref name="mapperAccessor"/> and
        /// returns its projection expression (<c>ToExpression(configure)</c>), with
        /// nested mappers inlined and variables substituted.
        /// </summary>
        private static LambdaExpression GetProjection(
            Expression mapperAccessor, Type sourceType, Type destType, object? configure)
        {
            var mapper = Evaluate(mapperAccessor)!;
            Type mapperType = typeof(Mapper<,>).MakeGenericType(sourceType, destType);
            MethodInfo toExpression = mapperType.GetMethod(nameof(Mapper<int, int>.ToExpression))!;
            return (LambdaExpression)toExpression.Invoke(mapper, [configure])!;
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
                return Expression.Condition(
                    Expression.Equal(source, Expression.Default(source.Type)),
                    Expression.Default(body.Type),
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

        /// <summary>
        /// Evaluates an expression that references no query parameters (a mapper
        /// accessor or an options lambda captured from the surrounding closure) by
        /// compiling and invoking it.
        /// </summary>
        private static object? Evaluate(Expression expression)
        {
            if (expression is ConstantExpression constant)
            {
                return constant.Value;
            }

            Func<object?> accessor = Expression
                .Lambda<Func<object?>>(Expression.Convert(expression, typeof(object)))
                .Compile();
            return accessor();
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
