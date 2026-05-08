using ArchPillar.Extensions.Operations;

namespace ArchPillar.Extensions.Commands;

/// <summary>
/// Dispatches commands through the shared command pipeline. The dispatcher is
/// the single public entry point for application code.
/// </summary>
/// <remarks>
/// All <c>SendAsync</c> overloads return an <see cref="OperationResult"/> /
/// <see cref="OperationResult{TValue}"/> — callers always await and observe
/// the outcome.
/// </remarks>
public interface ICommandDispatcher
{
    /// <summary>
    /// Dispatches a command that produces no payload.
    /// </summary>
    /// <param name="command">The command to dispatch.</param>
    /// <param name="cancellationToken">Cancellation propagated to middleware and the handler.</param>
    /// <returns>The operation outcome.</returns>
    Task<OperationResult> SendAsync(ICommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispatches a command that produces a payload of type
    /// <typeparamref name="TResult"/>.
    /// </summary>
    /// <typeparam name="TResult">The payload type.</typeparam>
    /// <param name="command">The command to dispatch.</param>
    /// <param name="cancellationToken">Cancellation propagated to middleware and the handler.</param>
    /// <returns>The operation outcome with payload.</returns>
    Task<OperationResult<TResult>> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispatches a batch of no-result commands.
    /// </summary>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <param name="commands">The commands to dispatch.</param>
    /// <param name="cancellationToken">Cancellation propagated to middleware and the handler.</param>
    /// <returns>
    /// A single <see cref="OperationResult"/>: success when the batch
    /// completed, otherwise the failure that aborted it.
    /// </returns>
    /// <remarks>
    /// <para>
    /// When an <see cref="IBatchCommandHandler{TCommand}"/> is registered the
    /// batch goes through the pipeline as one dispatch and the batch handler
    /// owns both validation and processing — wrapping middleware
    /// (transactions, retry, telemetry) covers the whole group.
    /// </para>
    /// <para>
    /// When no batch handler is registered, the dispatcher iterates the
    /// input list and calls
    /// <see cref="SendAsync(ICommand, CancellationToken)"/> per item.
    /// Each item runs its full pipeline pass — validation then handler —
    /// before the next item is considered. The loop bails on the first
    /// validation or handler failure and returns that failure; items
    /// already processed are not rolled back. There is no batch-level
    /// atomicity in this mode.
    /// </para>
    /// </remarks>
    Task<OperationResult> SendBatchAsync<TCommand>(
        IReadOnlyList<TCommand> commands,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand;

    /// <summary>
    /// Dispatches a batch of result-bearing commands.
    /// </summary>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <typeparam name="TResult">The payload type.</typeparam>
    /// <param name="commands">The commands to dispatch.</param>
    /// <param name="cancellationToken">Cancellation propagated to middleware and the handler.</param>
    /// <returns>
    /// A single <see cref="OperationResult{TValue}"/>: success carrying the
    /// per-command payloads in input order, otherwise the failure that
    /// aborted the batch.
    /// </returns>
    /// <remarks>
    /// <para>
    /// When an <see cref="IBatchCommandHandler{TCommand, TResult}"/> is
    /// registered the batch goes through the pipeline as one dispatch and
    /// the batch handler owns both validation and processing — wrapping
    /// middleware (transactions, retry, telemetry) covers the whole group.
    /// </para>
    /// <para>
    /// When no batch handler is registered, the dispatcher iterates the
    /// input list and calls
    /// <see cref="SendAsync{TResult}(ICommand{TResult}, CancellationToken)"/>
    /// per item. Each item runs its full pipeline pass — validation then
    /// handler — before the next item is considered. The loop bails on
    /// the first validation or handler failure and returns that failure;
    /// items already processed are not rolled back. There is no
    /// batch-level atomicity in this mode.
    /// </para>
    /// </remarks>
    Task<OperationResult<IReadOnlyList<TResult>>> SendBatchAsync<TCommand, TResult>(
        IReadOnlyList<TCommand> commands,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResult>;
}
