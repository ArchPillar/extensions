namespace ArchPillar.Mapper;

/// <summary>
/// Configures variable bindings for an in-memory <see cref="Mapper{TSource,TDest}.Map"/> call.
/// Optional properties are always included for in-memory mapping — null-safe
/// navigation guards handle any null intermediates automatically.
/// </summary>
/// <typeparam name="TDest">The destination type being mapped.</typeparam>
public sealed class MapOptions<TDest>
{
    private readonly Dictionary<object, object?> _variableBindings = [];

    internal IReadOnlyDictionary<object, object?> VariableBindings => _variableBindings;

    /// <summary>
    /// Binds a <see cref="Variable{T}"/> to a concrete value for this in-memory mapping call.
    /// Variables not bound here resolve to <c>default(T)</c>.
    /// </summary>
    public MapOptions<TDest> Set<T>(Variable<T> variable, T value)
    {
        _variableBindings[variable] = value;
        return this;
    }
}
