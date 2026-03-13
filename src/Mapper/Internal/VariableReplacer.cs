using System.Linq.Expressions;

namespace ArchPillar.Mapper.Internal;

/// <summary>
/// An expression visitor that replaces every <see cref="Variable{T}"/> conversion node
/// with a <see cref="ConstantExpression"/> containing the bound value (or the variable's
/// default value when no binding is present).
/// <para>
/// When a <see cref="Variable{T}"/> is used inside a mapping expression, the compiler
/// emits a <c>Convert</c> <see cref="System.Linq.Expressions.UnaryExpression"/> via the
/// implicit operator. This visitor intercepts those nodes and swaps them out before the
/// expression is compiled or sent to a query provider.
/// </para>
/// </summary>
internal sealed class VariableReplacer(Dictionary<object, object?> variableBindings)
    : ExpressionVisitor
{
    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType == ExpressionType.Convert && VariableHelper.IsVariableType(node.Operand.Type))
        {
            var variable = VariableHelper.TryExtractVariable(node.Operand);
            if (variable != null)
            {
                Type valueType = node.Type;

                if (variableBindings.TryGetValue(variable, out var boundValue))
                {
                    return Expression.Constant(boundValue, valueType);
                }

                // Variable is not set at the call site — fall back to its default value.
                return Expression.Constant(((IVariable)variable).GetDefaultValue(), valueType);
            }
        }

        return base.VisitUnary(node);
    }
}
