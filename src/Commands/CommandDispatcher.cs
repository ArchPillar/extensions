using ArchPillar.Extensions.Operations;
using ArchPillar.Extensions.Pipelines;

namespace ArchPillar.Extensions.Commands;

internal sealed class CommandDispatcher : ICommandDispatcher
{
    private readonly Pipeline<CommandContext> _pipeline;

    public CommandDispatcher(Pipeline<CommandContext> pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        _pipeline = pipeline;
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

        var requests = new IRequest[commands.Count];
        for (var i = 0; i < commands.Count; i++)
        {
            ArgumentNullException.ThrowIfNull(commands[i]);
            requests[i] = commands[i]!;
        }

        var context = new CommandContext(requests, typeof(TCommand));
        await _pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

        // Three success/failure shapes can land in context.Result:
        //   1. Batch handler returned a typed result → cast straight through.
        //   2. Any failure (validation, handler, exception middleware) → lift it.
        //   3. The router iterated per-item and every item succeeded → it left
        //      a generic Ok marker in Result and the per-item OperationResults
        //      in BatchResults. Compose the typed list from those.
        return context.Result switch
        {
            OperationResult<IReadOnlyList<TResult>> typed => typed,
            OperationFailure failure => failure,
            { IsSuccess: true } when context.BatchResults is { } perItem => ComposePerItem<TResult>(perItem),
            _ => OperationResult.Failure(
                OperationStatus.InternalServerError,
                "internal_error",
                "Internal Server Error",
                "Batch pipeline produced an unexpected result shape."),
        };
    }

    private static OperationResult<IReadOnlyList<TResult>> ComposePerItem<TResult>(IReadOnlyList<OperationResult> perItem)
    {
        var values = new TResult[perItem.Count];
        for (var i = 0; i < perItem.Count; i++)
        {
            if (perItem[i] is not OperationResult<TResult> typedItem)
            {
                return OperationResult.Failure(
                    OperationStatus.InternalServerError,
                    "internal_error",
                    "Internal Server Error",
                    $"Batch per-item result at index {i} was not OperationResult<{typeof(TResult).Name}>.");
            }

            values[i] = typedItem.Value!;
        }

        return OperationResult.Ok<IReadOnlyList<TResult>>(values);
    }
}
