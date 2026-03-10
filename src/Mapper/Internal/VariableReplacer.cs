using System.Linq.Expressions;
using System.Reflection;

namespace ArchPillar.Mapper.Internal;

/// <summary>
/// An expression visitor that replaces every <see cref="Variable{T}"/> conversion node
/// with a <see cref="ConstantExpression"/> containing the bound value (or the variable's
/// default value when no binding is present).
///
/// When a <see cref="Variable{T}"/> is used inside a mapping expression, the compiler
/// emits a <c>Convert</c> <see cref="System.Linq.Expressions.UnaryExpression"/> via the
/// implicit operator. This visitor intercepts those nodes and swaps them out before the
/// expression is compiled or sent to a query provider.
/// </summary>
internal sealed class VariableReplacer(Dictionary<object, object?> variableBindings)
    : ExpressionVisitor
{
    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType == ExpressionType.Convert && IsVariableType(node.Operand.Type))
        {
            var variable = TryExtractVariable(node.Operand);
            if (variable != null)
            {
                var valueType = node.Type; // T in Variable<T>

                if (variableBindings.TryGetValue(variable, out var boundValue))
                    return Expression.Constant(boundValue, valueType);

                // Variable is not set at the call site — fall back to its default value.
                return Expression.Constant(((IVariable)variable).GetDefaultValue(), valueType);
            }
        }

        return base.VisitUnary(node);
    }

    /// <summary>
    /// Extracts the <see cref="Variable{T}"/> instance from an operand expression.
    /// Returns <c>null</c> when the pattern is not recognised.
    /// </summary>
    private static object? TryExtractVariable(Expression operand)
    {
        // Case 1 — the variable is captured as a direct constant.
        if (operand is ConstantExpression constant)
            return constant.Value;

        // Case 2 — the variable is a property or field on a captured closure/context.
        if (operand is MemberExpression { Expression: ConstantExpression target } member)
        {
            return member.Member switch
            {
                PropertyInfo property => property.GetValue(target.Value),
                FieldInfo    field    => field.GetValue(target.Value),
                _                    => null,
            };
        }

        return null;
    }

    private static bool IsVariableType(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Variable<>);
}
