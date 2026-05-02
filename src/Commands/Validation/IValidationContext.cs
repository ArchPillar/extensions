using ArchPillar.Extensions.Primitives;

namespace ArchPillar.Extensions.Commands.Validation;

/// <summary>
/// Accumulator passed to a handler's
/// <see cref="ICommandHandler{TCommand}.ValidateAsync"/> implementation.
/// Validation extension helpers add errors to the context; the validation
/// middleware short-circuits the pipeline if any are present.
/// </summary>
public interface IValidationContext
{
    /// <summary>
    /// <c>true</c> when at least one error has been added.
    /// </summary>
    bool HasErrors { get; }

    /// <summary>
    /// The errors accumulated so far. Order is registration order.
    /// </summary>
    IReadOnlyList<OperationError> Errors { get; }

    /// <summary>
    /// Adds an error to the context.
    /// </summary>
    /// <param name="error">The error to add.</param>
    void AddError(OperationError error);
}
