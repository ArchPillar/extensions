using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ArchPillar.Extensions.EntityFrameworkCore.Npgsql.Internal.Functions;

/// <summary>
/// Rewrites <c>EF.Functions.ToJsonb(shape)</c> calls into the flat
/// <see cref="JsonbDbFunctions.JsonbBuildObjectCore(object?[])"/> marker before query
/// translation. The shape's member names (read from the expression tree, never from a
/// runtime instance) become the jsonb keys and the member value expressions become the
/// values, so the call translates to <c>jsonb_build_object('k', v, …)</c>.
/// </summary>
internal sealed class JsonbShapeInterceptor : IQueryExpressionInterceptor
{
    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
        => new Rewriter().Visit(queryExpression);

    private sealed class Rewriter : ExpressionVisitor
    {
        private static readonly MethodInfo _coreMethod =
            new Func<object?[], string>(JsonbDbFunctions.JsonbBuildObjectCore).Method;

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType != typeof(JsonbDbFunctions)
                || node.Method.Name != nameof(JsonbDbFunctions.ToJsonb))
            {
                return base.VisitMethodCall(node);
            }

            // Extension method: Arguments = [DbFunctions, shape].
            Expression shape = Unwrap(node.Arguments[1]);
            IReadOnlyList<KeyValuePair<string, Expression>> members = ExtractMembers(shape);

            var elements = new List<Expression>(members.Count * 2);
            foreach (KeyValuePair<string, Expression> member in members)
            {
                elements.Add(Expression.Constant(member.Key, typeof(object)));
                Expression visited = Visit(member.Value)!;
                elements.Add(visited.Type == typeof(object)
                    ? visited
                    : Expression.Convert(visited, typeof(object)));
            }

            NewArrayExpression array = Expression.NewArrayInit(typeof(object), elements);
            return Expression.Call(_coreMethod, array);
        }

        private static Expression Unwrap(Expression expression)
            => expression is UnaryExpression { NodeType: ExpressionType.Convert, Operand: { } inner }
                ? Unwrap(inner)
                : expression;

        private static IReadOnlyList<KeyValuePair<string, Expression>> ExtractMembers(Expression shape)
        {
            var result = new List<KeyValuePair<string, Expression>>();

            switch (shape)
            {
                case NewExpression { Members: { } members } newExpression:
                    for (var i = 0; i < members.Count; i++)
                    {
                        result.Add(new(members[i].Name, newExpression.Arguments[i]));
                    }

                    break;

                case MemberInitExpression memberInit:
                    if (memberInit.NewExpression.Members is { } ctorMembers)
                    {
                        for (var i = 0; i < ctorMembers.Count; i++)
                        {
                            result.Add(new(ctorMembers[i].Name, memberInit.NewExpression.Arguments[i]));
                        }
                    }

                    foreach (MemberBinding binding in memberInit.Bindings)
                    {
                        if (binding is not MemberAssignment assignment)
                        {
                            throw UnsupportedShape();
                        }

                        result.Add(new(assignment.Member.Name, assignment.Expression));
                    }

                    break;

                default:
                    throw UnsupportedShape();
            }

            if (result.Count == 0)
            {
                throw UnsupportedShape();
            }

            return result;
        }

        private static InvalidOperationException UnsupportedShape()
            => new(
                "EF.Functions.ToJsonb(shape) requires an inline object initializer — an anonymous type " +
                "(new { a = …, b = … }) or a named type with an object initializer (new MyDto { A = …, B = … }) " +
                "with at least one member. Positional constructors/records and pre-built instances are not supported.");
    }
}
