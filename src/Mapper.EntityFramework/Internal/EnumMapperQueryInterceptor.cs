using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ArchPillar.Extensions.Mapper.EntityFramework.Internal;

/// <summary>
/// Intercepts EF Core query compilation to rewrite
/// <c>EnumMapper&lt;TSource, TDest&gt;.Map(source)</c> instance calls into
/// <c>EnumMappingFunctions.MapEnum&lt;TSource, TDest&gt;(source)</c> static calls.
/// <para>
/// This is necessary because EF Core's <c>VisitMethodCall</c> rejects instance
/// methods whose receiver is a non-SQL object (like an <c>EnumMapper</c> constant).
/// By converting to a static method, the call reaches
/// <see cref="EnumMapperMethodCallTranslator"/>.
/// </para>
/// </summary>
internal sealed class EnumMapperQueryInterceptor(EnumMappingStore store) : IQueryExpressionInterceptor
{
    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        return new EnumMapperCallRewriter(store).Visit(queryExpression);
    }

    /// <summary>
    /// Expression visitor that finds <c>EnumMapper&lt;,&gt;.Map()</c> calls
    /// and replaces them with the static marker method.
    /// </summary>
    private sealed class EnumMapperCallRewriter(EnumMappingStore store) : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            // Only interested in instance calls named "Map" with one argument
            // on a type that is a closed generic of EnumMapper<,>.
            if (node.Object is not null
                && node.Method.Name == "Map"
                && node.Arguments.Count == 1
                && IsEnumMapperType(node.Method.DeclaringType))
            {
                var typeArgs = node.Method.DeclaringType!.GetGenericArguments();
                var sourceType = typeArgs[0];
                var destType = typeArgs[1];

                // Extract the EnumMapper instance from the expression tree
                // (it is a constant or a closure member-access chain) and
                // register its mapping table in the store.
                var mapperInstance = EvaluateExpression(node.Object);
                store.RegisterDynamic(mapperInstance, sourceType, destType);

                // Replace with the static marker:
                //   EnumMappingFunctions.MapEnum<TSource, TDest>(source)
                MethodInfo closedMethod = EnumMappingFunctions.MapEnumMethod
                    .MakeGenericMethod(sourceType, destType);

                return Expression.Call(closedMethod, Visit(node.Arguments[0]));
            }

            return base.VisitMethodCall(node);
        }

        private static bool IsEnumMapperType(Type? type)
        {
            return type?.IsGenericType == true
                && type.GetGenericTypeDefinition() == typeof(EnumMapper<,>);
        }

        /// <summary>
        /// Compiles and invokes a sub-expression to obtain its runtime value.
        /// Used to extract the <c>EnumMapper</c> instance from constant or
        /// closure-captured expressions.
        /// </summary>
        private static object EvaluateExpression(Expression expression)
        {
            return Expression.Lambda<Func<object>>(
                    Expression.Convert(expression, typeof(object)))
                .Compile()
                .Invoke();
        }
    }
}
