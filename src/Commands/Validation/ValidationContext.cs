using ArchPillar.Extensions.Operations;

namespace ArchPillar.Extensions.Commands.Validation;

/// <summary>
/// Default <see cref="IValidationContext"/> implementation backed by a
/// lazily-allocated list. No allocations are performed until the first error
/// is added.
/// </summary>
public sealed class ValidationContext : IValidationContext
{
    private List<ValidationEntry>? _entries;

    /// <inheritdoc/>
    public bool HasErrors => _entries is { Count: > 0 };

    /// <inheritdoc/>
    public IReadOnlyList<ValidationEntry> Entries
        => (IReadOnlyList<ValidationEntry>?)_entries ?? [];

    /// <inheritdoc/>
    public void AddError(string? field, OperationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        (_entries ??= []).Add(new ValidationEntry(field, error));
    }
}
