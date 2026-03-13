using System.Linq.Expressions;
using System.Reflection;

namespace ArchPillar.Mapper.Internal;

/// <summary>
/// An expression visitor that replaces every <see cref="Variable{T}"/> conversion node
/// with a runtime list-scan call: <c>GetVariable&lt;T&gt;(bindings, key, defaultValue)</c>.
/// <para>
/// The <paramref name="bindingsParam"/> parameter expression is wired as a second parameter
/// of the compiled delegate so the same compiled function can be reused across calls with
/// different variable bindings — eliminating per-call recompilation.
/// </para>
/// <para>
/// Passing <see langword="null"/> for the bindings list at invocation time causes every
/// variable to resolve to its declared <see cref="Variable{T}.DefaultValue"/>.
/// </para>
/// </summary>
internal sealed class VariableDictReplacer(ParameterExpression bindingsParam) : ExpressionVisitor
{
    internal static readonly Type BindingsType = typeof(List<ValueTuple<object, object?>>);

    private static readonly MethodInfo GetVariableMethod =
        typeof(VariableDictReplacer)
            .GetMethod(nameof(GetVariable), BindingFlags.Public | BindingFlags.Static)!;

    /// <summary>
    /// Returns the bound value from <paramref name="bindings"/> when present; otherwise
    /// returns <paramref name="defaultValue"/>.
    /// </summary>
    public static T GetVariable<T>(List<(object Key, object? Value)>? bindings, object key, T defaultValue)
    {
        if (bindings != null)
        {
            for (var i = 0; i < bindings.Count; i++)
            {
                if (ReferenceEquals(bindings[i].Key, key))
                {
                    return (T)bindings[i].Value!;
                }
            }
        }

        return defaultValue;
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType == ExpressionType.Convert && VariableHelper.IsVariableType(node.Operand.Type))
        {
            var variable = VariableHelper.TryExtractVariable(node.Operand);
            if (variable != null)
            {
                Type valueType = node.Type;
                var defaultValue = ((IVariable)variable).GetDefaultValue();

                return Expression.Call(
                    GetVariableMethod.MakeGenericMethod(valueType),
                    bindingsParam,
                    Expression.Constant(variable),
                    Expression.Constant(defaultValue, valueType));
            }
        }

        return base.VisitUnary(node);
    }
}
