using ArchPillar.Extensions.Commands.Internal;
using ArchPillar.Extensions.Operations;
using ArchPillar.Extensions.Pipelines;

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

    public async Task<OperationResult> SendBatchAsync<TCommand>(
        IReadOnlyList<TCommand> commands,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand
    {
        ArgumentNullException.ThrowIfNull(commands);

        if (commands.Count == 0)
        {
            return OperationResult.NoContent();
        }

        CommandInvokerDescriptor descriptor = _registry.Get(typeof(TCommand));

        if (descriptor.InvokeBatchAsync is null)
        {
            // No batch handler — fan out via SendAsync. Each item runs through
            // its own pipeline pass (independent validation, independent
            // transaction). Fail-fast on the first failure.
            for (var i = 0; i < commands.Count; i++)
            {
                ArgumentNullException.ThrowIfNull(commands[i]);
                OperationResult itemResult = await SendAsync(commands[i], cancellationToken).ConfigureAwait(false);
                if (itemResult.IsFailure)
                {
                    return itemResult;
                }
            }

            return OperationResult.NoContent();
        }

        var requests = new IRequest[commands.Count];
        for (var i = 0; i < commands.Count; i++)
        {
            ArgumentNullException.ThrowIfNull(commands[i]);
            requests[i] = commands[i]!;
        }

        var context = new CommandContext(requests, typeof(TCommand));
        await _pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

        return context.Result
            ?? OperationResult.Failure(
                OperationStatus.InternalServerError,
                "internal_error",
                "Internal Server Error",
                "Batch pipeline produced no result.");
    }

    public async Task<OperationResult<IReadOnlyList<TResult>>> SendBatchAsync<TCommand, TResult>(
        IReadOnlyList<TCommand> commands,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResult>
    {
        ArgumentNullException.ThrowIfNull(commands);

        if (commands.Count == 0)
        {
            return OperationResult.Ok<IReadOnlyList<TResult>>([]);
        }

        CommandInvokerDescriptor descriptor = _registry.Get(typeof(TCommand));

        if (descriptor.InvokeBatchAsync is null)
        {
            // Fan out via per-item SendAsync. Fail-fast on first failure.
            var collected = new TResult[commands.Count];
            for (var i = 0; i < commands.Count; i++)
            {
                ArgumentNullException.ThrowIfNull(commands[i]);
                OperationResult<TResult> itemResult = await SendAsync(commands[i], cancellationToken).ConfigureAwait(false);
                if (itemResult.IsFailure)
                {
                    return new OperationResult<IReadOnlyList<TResult>>
                    {
                        Status = itemResult.Status,
                        Problem = itemResult.Problem,
                        Exception = itemResult.Exception,
                    };
                }

                collected[i] = itemResult.Value!;
            }

            return OperationResult.Ok<IReadOnlyList<TResult>>(collected);
        }

        var requests = new IRequest[commands.Count];
        for (var i = 0; i < commands.Count; i++)
        {
            ArgumentNullException.ThrowIfNull(commands[i]);
            requests[i] = commands[i]!;
        }

        var context = new CommandContext(requests, typeof(TCommand));
        await _pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

        return context.Result switch
        {
            OperationResult<IReadOnlyList<TResult>> typed => typed,
            OperationFailure failure => failure,
            _ => OperationResult.Failure(
                OperationStatus.InternalServerError,
                "internal_error",
                "Internal Server Error",
                "Batch pipeline produced an unexpected result shape."),
        };
    }
}
