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
                && node.Arguments.Count >= 1
                && node.Arguments.Count <= 2)
            {
                Type? declaringType = node.Method.DeclaringType;

                // EnumMapper<TSource, TDest>.Map(...)
                if (node.Method.Name == "Map" && IsEnumMapperType(declaringType))
                {
                    Type[] typeArgs = declaringType!.GetGenericArguments();
                    return RewriteMapCall(node, typeArgs[0], typeArgs[1]);
                }

                // SymmetricEnumMapper<TLeft, TRight>.Map(...)
                if (node.Method.Name == "Map" && IsSymmetricEnumMapperType(declaringType))
                {
                    Type[] typeArgs = declaringType!.GetGenericArguments();
                    return RewriteMapCall(node, typeArgs[0], typeArgs[1]);
                }

                // SymmetricEnumMapper<TLeft, TRight>.MapReverse(...)
                // Reverse direction: source is TRight, dest is TLeft.
                if (node.Method.Name == "MapReverse" && IsSymmetricEnumMapperType(declaringType))
                {
                    Type[] typeArgs = declaringType!.GetGenericArguments();
                    return RewriteMapCall(node, typeArgs[1], typeArgs[0]);
                }
            }

            return base.VisitMethodCall(node);
        }

        private Expression RewriteMapCall(MethodCallExpression node, Type sourceType, Type destType)
        {
            Expression sourceArg = Visit(node.Arguments[0]);

            // 2-arg overload: Map(TSource?, TDest defaultValue)
            if (node.Arguments.Count == 2)
            {
                MethodInfo closedMethod = EnumMappingFunctions.MapEnumNullableWithDefaultMethod
                    .MakeGenericMethod(sourceType, destType);

                return Expression.Call(closedMethod, sourceArg, Visit(node.Arguments[1]));
            }

            // 1-arg with nullable source: Map(TSource?) → TDest?
            if (Nullable.GetUnderlyingType(sourceArg.Type) != null)
            {
                MethodInfo closedMethod = EnumMappingFunctions.MapEnumNullableMethod
                    .MakeGenericMethod(sourceType, destType);

                return Expression.Call(closedMethod, sourceArg);
            }

            // 1-arg non-nullable: Map(TSource) → TDest
            MethodInfo nonNullableMethod = EnumMappingFunctions.MapEnumMethod
                .MakeGenericMethod(sourceType, destType);

            return Expression.Call(nonNullableMethod, sourceArg);
        }

        private static bool IsEnumMapperType(Type? type)
        {
            return type?.IsGenericType == true
                && type.GetGenericTypeDefinition() == typeof(EnumMapper<,>);
        }

        private static bool IsSymmetricEnumMapperType(Type? type)
        {
            return type?.IsGenericType == true
                && type.GetGenericTypeDefinition() == typeof(SymmetricEnumMapper<,>);
        }
    }
}
