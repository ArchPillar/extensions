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
/// Per-command <see cref="ICommandHandler{TCommand}.ValidateAsync"/> still
/// runs first; only the commands that pass validation are forwarded to
/// <see cref="HandleBatchAsync"/>. The returned list aligns 1:1 with the input
/// list — entries for invalid commands carry the validation result.
/// </remarks>
public interface IBatchCommandHandler<in TCommand> : IRequestHandler
    where TCommand : ICommand
{
    /// <summary>
    /// Processes a batch of commands.
    /// </summary>
    /// <param name="commands">The commands that passed validation.</param>
    /// <param name="cancellationToken">Cancellation propagated from the dispatcher.</param>
    /// <returns>A task producing one outcome per command, in input order.</returns>
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
    /// Processes a batch of commands.
    /// </summary>
    /// <param name="commands">The commands that passed validation.</param>
    /// <param name="cancellationToken">Cancellation propagated from the dispatcher.</param>
    /// <returns>A task producing one outcome per command, in input order.</returns>
    Task<IReadOnlyList<OperationResult<TResult>>> HandleBatchAsync(
        IReadOnlyList<TCommand> commands,
        CancellationToken cancellationToken);
}
