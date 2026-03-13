using System.Linq.Expressions;

namespace ArchPillar.Extensions.Mapper.Internal;

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
internal sealed class SelectorCompiler(ParameterExpression? dictParam = null) : ExpressionVisitor
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
