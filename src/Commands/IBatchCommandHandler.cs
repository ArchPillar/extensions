using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Operations;

namespace ArchPillar.Extensions.Commands;

/// <summary>
/// Optional opt-in for processing a batch of commands as a single unit. When
/// registered alongside an <see cref="ICommandHandler{TCommand}"/>, the
/// dispatcher's <c>SendBatchAsync</c> uses this handler instead of fanning
/// out one-by-one. The batch handler owns both validation and handling for
/// the batch — the per-command <see cref="ICommandHandler{TCommand}"/> is
/// not consulted on the batch path.
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <remarks>
/// Batch dispatch is all-or-nothing: <see cref="ValidateAsync"/> sees the
/// whole input list and either accepts it (returning without populating the
/// validation context) or rejects the whole batch in one go.
/// <see cref="HandleBatchAsync"/> is invoked only when validation passed and
/// can assume the input is acceptable.
/// </remarks>
public interface IBatchCommandHandler<in TCommand> : IRequestHandler
    where TCommand : ICommand
{
    /// <summary>
    /// Validates the batch as a single unit. Default implementation is a
    /// no-op; override to populate <paramref name="validation"/> with any
    /// shape, content, or cross-item errors that should reject the batch.
    /// </summary>
    /// <param name="commands">The full input list.</param>
    /// <param name="validation">The accumulator for validation errors.</param>
    /// <param name="cancellationToken">Cancellation propagated from the dispatcher.</param>
    Task ValidateAsync(
        IReadOnlyList<TCommand> commands,
        IValidationContext validation,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>
    /// Processes the batch. Invoked only when <see cref="ValidateAsync"/>
    /// produced no errors.
    /// </summary>
    /// <param name="commands">The full input list, in the original order.</param>
    /// <param name="cancellationToken">Cancellation propagated from the dispatcher.</param>
    /// <returns>
    /// A single <see cref="OperationResult"/> describing whether the batch
    /// succeeded.
    /// </returns>
    Task<OperationResult> HandleBatchAsync(
        IReadOnlyList<TCommand> commands,
        CancellationToken cancellationToken);
}

/// <summary>
/// Optional opt-in batch handler for commands that produce a payload.
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <typeparam name="TResult">The payload type returned on success.</typeparam>
/// <remarks>
/// Batch dispatch is all-or-nothing: <see cref="ValidateAsync"/> sees the
/// whole input list and either accepts it or rejects the whole batch.
/// <see cref="HandleBatchAsync"/> returns one
/// <see cref="OperationResult{TValue}"/> wrapping the per-command results in
/// input order; the dispatcher never partitions or reorders them.
/// </remarks>
public interface IBatchCommandHandler<in TCommand, TResult> : IRequestHandler
    where TCommand : ICommand<TResult>
{
    /// <summary>
    /// Validates the batch as a single unit.
    /// </summary>
    /// <param name="commands">The full input list.</param>
    /// <param name="validation">The accumulator for validation errors.</param>
    /// <param name="cancellationToken">Cancellation propagated from the dispatcher.</param>
    Task ValidateAsync(
        IReadOnlyList<TCommand> commands,
        IValidationContext validation,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>
    /// Processes the batch. Invoked only when <see cref="ValidateAsync"/>
    /// produced no errors.
    /// </summary>
    /// <param name="commands">The full input list, in the original order.</param>
    /// <param name="cancellationToken">Cancellation propagated from the dispatcher.</param>
    /// <returns>
    /// A single <see cref="OperationResult{TValue}"/> wrapping per-command
    /// payloads in input order. Failure short-circuits the whole batch.
    /// </returns>
    Task<OperationResult<IReadOnlyList<TResult>>> HandleBatchAsync(
        IReadOnlyList<TCommand> commands,
        CancellationToken cancellationToken);
}
