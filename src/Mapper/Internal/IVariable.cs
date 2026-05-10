namespace ArchPillar.Extensions.Mapper.Internal;

/// <summary>
/// Non-generic interface implemented by <see cref="Variable{T}"/> so that internal
/// code can read the default value without reflection.
/// </summary>
internal interface IVariable
{
    /// <summary>
    /// Returns <see cref="Variable{T}.DefaultValue"/> boxed as <c>object?</c>.
    /// </summary>
    public object? GetDefaultValue();
}
