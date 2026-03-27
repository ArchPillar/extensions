using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ArchPillar.Extensions.Mapper.EntityFrameworkCore.Internal;

internal sealed class EnumMapperQueryInterceptor : IQueryExpressionInterceptor
{
    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        return new EnumMapperCallRewriter().Visit(queryExpression);
    }

    private sealed class EnumMapperCallRewriter : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Object is not null
                && node.Method.Name == "Map"
                && node.Arguments.Count >= 1
                && node.Arguments.Count <= 2
                && IsEnumMapperType(node.Method.DeclaringType))
            {
                Type[] typeArgs = node.Method.DeclaringType!.GetGenericArguments();
                Expression sourceArg = Visit(node.Arguments[0]);

                // 2-arg overload: Map(TSource?, TDest defaultValue)
                if (node.Arguments.Count == 2)
                {
                    MethodInfo closedMethod = EnumMappingFunctions.MapEnumNullableWithDefaultMethod
                        .MakeGenericMethod(typeArgs[0], typeArgs[1]);

                    return Expression.Call(closedMethod, sourceArg, Visit(node.Arguments[1]));
                }

                // 1-arg with nullable source: Map(TSource?) → TDest?
                if (Nullable.GetUnderlyingType(sourceArg.Type) != null)
                {
                    MethodInfo closedMethod = EnumMappingFunctions.MapEnumNullableMethod
                        .MakeGenericMethod(typeArgs[0], typeArgs[1]);

                    return Expression.Call(closedMethod, sourceArg);
                }

                // 1-arg non-nullable: Map(TSource) → TDest (existing path)
                MethodInfo nonNullableMethod = EnumMappingFunctions.MapEnumMethod
                    .MakeGenericMethod(typeArgs[0], typeArgs[1]);

                return Expression.Call(nonNullableMethod, sourceArg);
            }

            return base.VisitMethodCall(node);
        }

        private static bool IsEnumMapperType(Type? type)
        {
            return type?.IsGenericType == true
                && type.GetGenericTypeDefinition() == typeof(EnumMapper<,>);
        }
    }
}
