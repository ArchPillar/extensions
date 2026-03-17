namespace ArchPillar.Extensions.Mapper;

/// <summary>
/// Provides helper methods used by the AOT source generator to resolve
/// <see cref="Variable{T}"/> bindings at runtime without expression compilation.
/// </summary>
public static class AotHelper
{
    /// <summary>
    /// Resolves a <see cref="Variable{T}"/> from the bindings list by reference
    /// identity, falling back to <paramref name="defaultValue"/> when the variable
    /// is not bound (or when <paramref name="bindings"/> is <see langword="null"/>).
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
}
