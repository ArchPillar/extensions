using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Operations;

namespace ArchPillar.Extensions.Commands;

/// <summary>
/// Handles a command that produces no payload — only a status carried in the
/// returned <see cref="OperationResult"/>. The dispatcher still awaits the
/// handler and returns the outcome to the caller.
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <remarks>
/// Validation lives on the handler so that handlers can load entities from
/// storage and validate against persisted state. The default
/// <see cref="ValidateAsync"/> is a no-op; override it (or, more typically,
/// derive from <see cref="CommandHandlerBase{TCommand}"/>) to add per-command
/// rules.
/// </remarks>
public interface ICommandHandler<in TCommand> : IRequestHandler
    where TCommand : ICommand
{
    /// <summary>
    /// Validates the command before <see cref="HandleAsync"/> runs. Errors
    /// accumulated on <paramref name="context"/> short-circuit the pipeline
    /// with <see cref="OperationStatus.UnprocessableEntity"/>.
    /// </summary>
    /// <param name="command">The command to validate.</param>
    /// <param name="context">A context for accumulating validation errors.</param>
    /// <param name="cancellationToken">Cancellation propagated from the dispatcher.</param>
    /// <returns>A task that completes when validation is done.</returns>
    Task ValidateAsync(TCommand command, IValidationContext context, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>
    /// Executes the command. Returns an <see cref="OperationResult"/> describing
    /// the outcome. Throwing an <see cref="OperationException"/> (e.g. via
    /// <c>throw OperationResult.NotFound(...)</c>) is equivalent to returning
    /// the carried result.
    /// </summary>
    /// <param name="command">The command to execute.</param>
    /// <param name="cancellationToken">Cancellation propagated from the dispatcher.</param>
    /// <returns>A task producing the operation outcome.</returns>
    Task<OperationResult> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// Handles a command that produces a payload of type <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <typeparam name="TResult">The payload type returned on success.</typeparam>
public interface ICommandHandler<in TCommand, TResult> : IRequestHandler
    where TCommand : ICommand<TResult>
{
    /// <inheritdoc cref="ICommandHandler{TCommand}.ValidateAsync"/>
    Task ValidateAsync(TCommand command, IValidationContext context, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>
    /// Executes the command. Returns an
    /// <see cref="OperationResult{TValue}"/> carrying the payload on success.
    /// </summary>
    /// <param name="command">The command to execute.</param>
    /// <param name="cancellationToken">Cancellation propagated from the dispatcher.</param>
    /// <returns>A task producing the operation outcome with payload.</returns>
    Task<OperationResult<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken);
}
