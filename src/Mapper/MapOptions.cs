namespace ArchPillar.Mapper;

/// <summary>
/// Configures variable bindings for an in-memory <see cref="Mapper{TSource,TDest}.Map(TSource)"/> call.
/// Optional properties are always included for in-memory mapping — null-safe
/// navigation guards handle any null intermediates automatically.
/// </summary>
/// <typeparam name="TDest">The destination type being mapped.</typeparam>
public sealed class MapOptions<TDest>
{
    internal List<(object Key, object? Value)> VariableBindings { get; } = [];

    /// <summary>
    /// Binds a <see cref="Variable{T}"/> to a concrete value for this in-memory mapping call.
    /// Variables not bound here resolve to <c>default(T)</c>.
    /// </summary>
    public MapOptions<TDest> Set<T>(Variable<T> variable, T value)
    {
        VariableBindings.Add((variable, value));
        return this;
    }
}
