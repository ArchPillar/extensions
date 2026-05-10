using ArchPillar.Extensions.Operations;

namespace ArchPillar.Extensions.Commands.Validation;

/// <summary>
/// Accumulator passed to a handler's
/// <see cref="ICommandHandler{TCommand}.ValidateAsync"/> implementation.
/// Validators add errors with an associated field; the dispatcher converts
/// the accumulated entries into an <see cref="OperationProblem"/> at the
/// boundary (errors keyed by field, top-level status by precedence).
/// </summary>
public interface IValidationContext
{
    /// <summary><c>true</c> when at least one error has been added.</summary>
    public bool HasErrors { get; }

    /// <summary>The accumulated entries so far.</summary>
    public IReadOnlyList<ValidationEntry> Entries { get; }

    /// <summary>
    /// Adds an error to the context.
    /// </summary>
    /// <param name="field">
    /// The field this error is attached to. <c>null</c> for top-level errors
    /// (auth, etc.) — those promote to <see cref="OperationProblem.Title"/>
    /// and <see cref="OperationProblem.Detail"/> at the boundary.
    /// </param>
    /// <param name="error">The error to add.</param>
    public void AddError(string? field, OperationError error);
}

/// <summary>
/// One accumulated entry inside an <see cref="IValidationContext"/>.
/// </summary>
/// <param name="Field">The field name, or <c>null</c> for top-level errors.</param>
/// <param name="Error">The error item.</param>
public readonly record struct ValidationEntry(string? Field, OperationError Error);
