using System.Linq.Expressions;
using System.Reflection;

namespace ArchPillar.Extensions.Mapper.Internal;

/// <summary>
/// Shared helpers for detecting and extracting <see cref="Variable{T}"/>
/// instances from expression tree nodes. Used by both
/// <see cref="VariableReplacer"/> and <see cref="VariableDictReplacer"/>.
/// </summary>
internal static class VariableHelper
{
    /// <summary>
    /// Extracts the <see cref="Variable{T}"/> instance from an operand expression.
    /// Returns <c>null</c> when the pattern is not recognised.
    /// </summary>
    internal static object? TryExtractVariable(Expression operand)
    {
        // Case 1 — the variable is captured as a direct constant.
        if (operand is ConstantExpression constant)
        {
            return constant.Value;
        }

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

    internal static bool IsVariableType(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Variable<>);
}
