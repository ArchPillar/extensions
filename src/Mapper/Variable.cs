namespace ArchPillar.Mapper;

/// <summary>
/// A typed placeholder that can appear in mapping expressions and is substituted
/// with an actual value (or <c>default(T)</c>) at projection / mapping time.
/// Declare as a property on your <see cref="MapperContext"/> subclass so that
/// callers can reference it by name — no magic strings.
/// </summary>
/// <typeparam name="T">The value type the variable holds.</typeparam>
public sealed class Variable<T>
{
    /// <summary>
    /// Allows a <see cref="Variable{T}"/> to appear directly inside expression
    /// bodies (e.g. <c>src.OwnerId == CurrentUserId</c>) without an explicit cast.
    /// The expression visitor replaces this conversion node with a
    /// <see cref="System.Linq.Expressions.ConstantExpression"/> at call time.
    /// </summary>
    public static implicit operator T(Variable<T> variable)
        => throw new InvalidOperationException(
            $"{nameof(Variable<T>)} must only be used inside mapping expressions. " +
            $"It cannot be invoked directly.");
}
