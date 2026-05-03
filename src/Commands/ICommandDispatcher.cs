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
    /// Dispatches a batch of no-result commands. If a
    /// <see cref="IBatchCommandHandler{TCommand}"/> is registered for
    /// <typeparamref name="TCommand"/> it is used; otherwise the dispatcher
    /// fans out to the single-command handler.
    /// </summary>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <param name="commands">The commands to dispatch.</param>
    /// <param name="cancellationToken">Cancellation propagated to middleware and the handler.</param>
    /// <returns>One outcome per command, in input order.</returns>
    Task<IReadOnlyList<OperationResult>> SendBatchAsync<TCommand>(
        IReadOnlyList<TCommand> commands,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand;

    /// <summary>
    /// Dispatches a batch of result-bearing commands. If a
    /// <see cref="IBatchCommandHandler{TCommand, TResult}"/> is registered for
    /// <typeparamref name="TCommand"/> it is used; otherwise the dispatcher
    /// fans out to the single-command handler.
    /// </summary>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <typeparam name="TResult">The payload type.</typeparam>
    /// <param name="commands">The commands to dispatch.</param>
    /// <param name="cancellationToken">Cancellation propagated to middleware and the handler.</param>
    /// <returns>One outcome per command, in input order.</returns>
    Task<IReadOnlyList<OperationResult<TResult>>> SendBatchAsync<TCommand, TResult>(
        IReadOnlyList<TCommand> commands,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResult>;
}
