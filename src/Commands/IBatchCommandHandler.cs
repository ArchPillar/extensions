using ArchPillar.Extensions.Operations;

namespace ArchPillar.Extensions.Commands;

/// <summary>
/// Optional opt-in for processing a batch of commands as a single unit. When
/// registered alongside an <see cref="ICommandHandler{TCommand}"/>, the
/// dispatcher's <c>SendBatchAsync</c> uses this handler instead of fanning out
/// one-by-one.
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <remarks>
/// Batch dispatch is all-or-nothing at the validation gate. The dispatcher
/// runs <see cref="ICommandHandler{TCommand}.ValidateAsync"/> for every input
/// command first; if <em>any</em> command fails validation, the whole batch
/// is rejected and <see cref="HandleBatchAsync"/> is never invoked. When
/// validation passes for every command, the handler receives the full input
/// list in the original order.
/// </remarks>
public interface IBatchCommandHandler<in TCommand> : IRequestHandler
    where TCommand : ICommand
{
    /// <summary>
    /// Processes a batch of commands. Only invoked when every command in the
    /// batch passed validation — the handler can assume each input is
    /// individually valid.
    /// </summary>
    /// <param name="commands">
    /// The full set of commands the caller passed to <c>SendBatchAsync</c>,
    /// in the same order.
    /// </param>
    /// <param name="cancellationToken">Cancellation propagated from the dispatcher.</param>
    /// <returns>
    /// One outcome per element of <paramref name="commands"/>, in the same
    /// order.
    /// </returns>
    Task<IReadOnlyList<OperationResult>> HandleBatchAsync(
        IReadOnlyList<TCommand> commands,
        CancellationToken cancellationToken);
}

/// <summary>
/// Optional opt-in batch handler for commands that produce a payload.
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <typeparam name="TResult">The payload type returned on success.</typeparam>
public interface IBatchCommandHandler<in TCommand, TResult> : IRequestHandler
    where TCommand : ICommand<TResult>
{
    /// <summary>
    /// Processes a batch of commands. Only invoked when every command in the
    /// batch passed validation — the handler can assume each input is
    /// individually valid.
    /// </summary>
    /// <param name="commands">
    /// The full set of commands the caller passed to <c>SendBatchAsync</c>,
    /// in the same order.
    /// </param>
    /// <param name="cancellationToken">Cancellation propagated from the dispatcher.</param>
    /// <returns>
    /// One outcome per element of <paramref name="commands"/>, in the same
    /// order.
    /// </returns>
    Task<IReadOnlyList<OperationResult<TResult>>> HandleBatchAsync(
        IReadOnlyList<TCommand> commands,
        CancellationToken cancellationToken);
}
