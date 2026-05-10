namespace ArchPillar.Extensions.Identifiers;

/// <summary>
/// Marker interface for all <see cref="Id{T}"/> values. Exposes the underlying
/// <see cref="Guid"/> so non-generic infrastructure can read the value without
/// needing the phantom type parameter.
/// </summary>
public interface IId
{
    /// <summary>The underlying Guid value.</summary>
    public Guid Value { get; }
}
