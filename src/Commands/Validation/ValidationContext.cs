using ArchPillar.Extensions.Primitives;

namespace ArchPillar.Extensions.Commands.Validation;

/// <summary>
/// Default <see cref="IValidationContext"/> implementation backed by a
/// lazily-allocated list. No allocations are performed until the first error
/// is added.
/// </summary>
public sealed class ValidationContext : IValidationContext
{
    private List<OperationError>? _errors;

    /// <inheritdoc/>
    public bool HasErrors => _errors is { Count: > 0 };

    /// <inheritdoc/>
    public IReadOnlyList<OperationError> Errors
        => (IReadOnlyList<OperationError>?)_errors ?? [];

    /// <inheritdoc/>
    public void AddError(OperationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        (_errors ??= []).Add(error);
    }
}
