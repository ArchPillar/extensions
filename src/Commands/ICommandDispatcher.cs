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
    /// The batch always runs through the pipeline once as a single dispatch,
    /// so wrapping middleware (transactions, retry, telemetry, exception)
    /// covers the whole group regardless of how the items are processed.
    /// </para>
    /// <para>
    /// When an <see cref="IBatchCommandHandler{TCommand}"/> is registered the
    /// router calls its <c>ValidateAsync</c> over the whole list and, on
    /// success, its <c>HandleBatchAsync</c>. The batch handler owns both
    /// validation and processing.
    /// </para>
    /// <para>
    /// When no batch handler is registered, the router iterates the input
    /// list internally and runs the per-command
    /// <see cref="ICommandHandler{TCommand}"/> for each item — validation,
    /// then handler — before moving on. The loop bails on the first
    /// validation or handler failure and surfaces that failure verbatim.
    /// Items already processed are not rolled back unless your wrapping
    /// middleware does so (e.g. a transaction middleware seeing the failed
    /// <see cref="OperationResult"/>).
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
    /// The batch always runs through the pipeline once as a single dispatch,
    /// so wrapping middleware (transactions, retry, telemetry, exception)
    /// covers the whole group regardless of how the items are processed.
    /// </para>
    /// <para>
    /// When an <see cref="IBatchCommandHandler{TCommand, TResult}"/> is
    /// registered the router calls its <c>ValidateAsync</c> over the whole
    /// list and, on success, its <c>HandleBatchAsync</c>. The batch handler
    /// owns both validation and processing.
    /// </para>
    /// <para>
    /// When no batch handler is registered, the router iterates the input
    /// list internally and runs the per-command
    /// <see cref="ICommandHandler{TCommand, TResult}"/> for each item —
    /// validation, then handler — before moving on. The loop bails on the
    /// first validation or handler failure and surfaces that failure
    /// verbatim. On full success the dispatcher composes the per-command
    /// payloads into the returned <see cref="OperationResult{TValue}"/>.
    /// </para>
    /// </remarks>
    Task<OperationResult<IReadOnlyList<TResult>>> SendBatchAsync<TCommand, TResult>(
        IReadOnlyList<TCommand> commands,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResult>;
}
