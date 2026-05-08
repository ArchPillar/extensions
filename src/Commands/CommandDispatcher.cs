using ArchPillar.Extensions.Commands.Internal;
using ArchPillar.Extensions.Pipelines;
using ArchPillar.Extensions.Operations;

namespace ArchPillar.Extensions.Commands;

internal sealed class CommandDispatcher : ICommandDispatcher
{
    private readonly Pipeline<CommandContext> _pipeline;
    private readonly CommandInvokerRegistry _registry;

    public CommandDispatcher(
        Pipeline<CommandContext> pipeline,
        CommandInvokerRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(registry);
        _pipeline = pipeline;
        _registry = registry;
    }

    public async Task<OperationResult> SendAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var context = new CommandContext(command);
        await _pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

        return context.Result
            ?? OperationResult.Failure(
                OperationStatus.InternalServerError,
                "internal_error",
                "Internal Server Error",
                "Command pipeline produced no result.");
    }

    public async Task<OperationResult<TResult>> SendAsync<TResult>(
        ICommand<TResult> command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var context = new CommandContext(command);
        await _pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

        // Failure paths (validation, exception middleware) all produce
        // OperationFailure, which implicitly converts to OperationResult<TResult>.
        // Success path is OperationResult<TResult> straight from the handler.
        return context.Result switch
        {
            OperationResult<TResult> typed => typed,
            OperationFailure failure => failure,
            _ => OperationResult.Failure(
                OperationStatus.InternalServerError,
                "internal_error",
                "Internal Server Error",
                "Command pipeline produced no result."),
        };
    }

    public async Task<IReadOnlyList<OperationResult>> SendBatchAsync<TCommand>(
        IReadOnlyList<TCommand> commands,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand
    {
        ArgumentNullException.ThrowIfNull(commands);

        if (commands.Count == 0)
        {
            return [];
        }

        CommandInvokerDescriptor descriptor = _registry.Get(typeof(TCommand));

        if (descriptor.InvokeBatchAsync is null)
        {
            // No batch handler — fan out as independent dispatches so each
            // command flows through its own pipeline pass (per-item
            // middleware, per-item transaction).
            var fanned = new OperationResult[commands.Count];
            for (var i = 0; i < commands.Count; i++)
            {
                fanned[i] = await SendAsync(commands[i], cancellationToken).ConfigureAwait(false);
            }

            return fanned;
        }

        var requests = new IRequest[commands.Count];
        for (var i = 0; i < commands.Count; i++)
        {
            ArgumentNullException.ThrowIfNull(commands[i]);
            requests[i] = commands[i]!;
        }

        var context = new CommandContext(requests, typeof(TCommand));
        await _pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

        return context.BatchResults
            ?? throw new InvalidOperationException(
                $"Batch dispatch for {typeof(TCommand).FullName} produced no per-item results.");
    }

    public async Task<IReadOnlyList<OperationResult<TResult>>> SendBatchAsync<TCommand, TResult>(
        IReadOnlyList<TCommand> commands,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResult>
    {
        ArgumentNullException.ThrowIfNull(commands);

        if (commands.Count == 0)
        {
            return [];
        }

        CommandInvokerDescriptor descriptor = _registry.Get(typeof(TCommand));

        if (descriptor.InvokeBatchAsync is null)
        {
            var fanned = new OperationResult<TResult>[commands.Count];
            for (var i = 0; i < commands.Count; i++)
            {
                fanned[i] = await SendAsync(commands[i], cancellationToken).ConfigureAwait(false);
            }

            return fanned;
        }

        var requests = new IRequest[commands.Count];
        for (var i = 0; i < commands.Count; i++)
        {
            ArgumentNullException.ThrowIfNull(commands[i]);
            requests[i] = commands[i]!;
        }

        var context = new CommandContext(requests, typeof(TCommand));
        await _pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<OperationResult> raw = context.BatchResults
            ?? throw new InvalidOperationException(
                $"Batch dispatch for {typeof(TCommand).FullName} produced no per-item results.");

        var typed = new OperationResult<TResult>[raw.Count];
        for (var i = 0; i < raw.Count; i++)
        {
            typed[i] = raw[i] switch
            {
                OperationResult<TResult> t => t,
                OperationFailure f => f,
                _ => OperationResult.Failure(
                    OperationStatus.InternalServerError,
                    "internal_error",
                    "Internal Server Error",
                    "Batch produced an unexpected non-typed, non-failure result."),
            };
        }

        return typed;
    }
}
