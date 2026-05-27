using System.Linq.Expressions;

namespace ArchPillar.Extensions.Mapper.Internal;

/// <summary>
/// An expression visitor that replaces every <see cref="Variable{T}"/> conversion node
/// with a property-access expression that reads the bound value (or the variable's
/// default value when no binding is present) from a <see cref="VariableValueBox{T}"/>.
/// <para>
/// When a <see cref="Variable{T}"/> is used inside a mapping expression, the compiler
/// emits a <c>Convert</c> <see cref="System.Linq.Expressions.UnaryExpression"/> via the
/// implicit operator. This visitor intercepts those nodes and swaps them out before the
/// expression is compiled or sent to a query provider.
/// </para>
/// <para>
/// The replacement is <c>((VariableValueBox&lt;T&gt;)box).Value</c> — a captured-closure
/// pattern that EF Core translates to a SQL parameter rather than an inline literal.
/// This preserves query-plan reuse and removes any chance of the value appearing in
/// the SQL text itself.
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

                var value = variableBindings.TryGetValue(variable, out var boundValue)
                    ? boundValue
                    : ((IVariable)variable).GetDefaultValue();

                Type boxType = typeof(VariableValueBox<>).MakeGenericType(valueType);
                var box = Activator.CreateInstance(boxType, value)!;

                return Expression.Property(
                    Expression.Constant(box, boxType),
                    boxType.GetProperty(nameof(VariableValueBox<object>.Value))!);
            }
        }

        return base.VisitUnary(node);
    }
}
