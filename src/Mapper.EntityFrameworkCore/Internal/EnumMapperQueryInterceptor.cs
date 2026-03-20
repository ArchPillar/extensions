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
                && node.Arguments.Count == 1
                && IsEnumMapperType(node.Method.DeclaringType))
            {
                Type[] typeArgs = node.Method.DeclaringType!.GetGenericArguments();

                MethodInfo closedMethod = EnumMappingFunctions.MapEnumMethod
                    .MakeGenericMethod(typeArgs[0], typeArgs[1]);

                return Expression.Call(closedMethod, Visit(node.Arguments[0]));
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
