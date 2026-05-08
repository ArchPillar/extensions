using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Operations;
using ArchPillar.Extensions.Pipelines;

namespace ArchPillar.Extensions.Commands.Internal;

/// <summary>
/// Terminal handler for the command pipeline. Resolves the
/// <see cref="CommandInvokerDescriptor"/> for the dispatched command type,
/// runs validation, and invokes the handler. Both calls happen at the
/// innermost point of the pipeline so any user-supplied wrapping middleware
/// (transactions, unit-of-work, retry, distributed locks) is in scope for
/// validation as well as execution.
/// </summary>
internal sealed class CommandRouter : IPipelineHandler<CommandContext>
{
    private readonly CommandInvokerRegistry _registry;
    private readonly IServiceProvider _services;

    public CommandRouter(CommandInvokerRegistry registry, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(services);
        _registry = registry;
        _services = services;
    }

    public Task HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        CommandInvokerDescriptor descriptor = _registry.Get(context.CommandType);

        return context.BatchCommands is { } batch
            ? HandleBatchAsync(descriptor, context, batch, cancellationToken)
            : HandleSingleAsync(descriptor, context, cancellationToken);
    }

    private async Task HandleSingleAsync(
        CommandInvokerDescriptor descriptor,
        CommandContext context,
        CancellationToken cancellationToken)
    {
        if (context.Command is not { } command)
        {
            context.Result = OperationResult.Failure(
                OperationStatus.InternalServerError,
                "internal_error",
                "Internal Server Error",
                "Single-dispatch command context had no command.");
            return;
        }

        // Validation runs inside whatever wrapping the user has placed around
        // the router (typically a transaction). This keeps entity-existence
        // and entity-state checks consistent with the snapshot the handler
        // writes against — no TOCTOU between validation read and handler
        // write.
        await descriptor.ValidateAsync(_services, command, context.Validation, cancellationToken).ConfigureAwait(false);
        OperationFailure? failure = context.Validation.ToFailureResult();
        if (failure is not null)
        {
            context.Result = failure;
            return;
        }

        OperationResult result = await descriptor.InvokeAsync(_services, command, cancellationToken).ConfigureAwait(false);
        context.Result = result;
    }

    private Task HandleBatchAsync(
        CommandInvokerDescriptor descriptor,
        CommandContext context,
        IReadOnlyList<IRequest> commands,
        CancellationToken cancellationToken)
    {
        // Two batch shapes — both terminate inside the router so wrapping
        // middleware (transactions, retry, telemetry, exception) covers the
        // whole batch as a single dispatch.
        return descriptor.ValidateBatchAsync is not null && descriptor.InvokeBatchAsync is not null
            ? HandleBatchHandlerAsync(descriptor, context, commands, cancellationToken)
            : IterateBatchAsync(descriptor, context, commands, cancellationToken);
    }

    private async Task HandleBatchHandlerAsync(
        CommandInvokerDescriptor descriptor,
        CommandContext context,
        IReadOnlyList<IRequest> commands,
        CancellationToken cancellationToken)
    {
        // Batch handler path: the handler owns both validation and processing.
        // The single-command ICommandHandler<TCommand> is not consulted.
        await descriptor.ValidateBatchAsync!(_services, commands, context.Validation, cancellationToken).ConfigureAwait(false);
        OperationFailure? failure = context.Validation.ToFailureResult();
        if (failure is not null)
        {
            context.Result = failure;
            return;
        }

        OperationResult result = await descriptor.InvokeBatchAsync!(_services, commands, cancellationToken).ConfigureAwait(false);
        context.Result = result;
    }

    private async Task IterateBatchAsync(
        CommandInvokerDescriptor descriptor,
        CommandContext context,
        IReadOnlyList<IRequest> commands,
        CancellationToken cancellationToken)
    {
        // No batch handler — iterate the input list and run the per-command
        // ICommandHandler<TCommand> for each item: validation, then handler.
        // Bail on the first failure (validation OR handler) and surface that
        // failure verbatim. On full success, the typed dispatcher needs the
        // per-item OperationResult<TResult>s to compose the
        // IReadOnlyList<TResult>; non-result commands don't, so skip the
        // O(N) allocation in that case.
        var perItem = descriptor.ProducesResult ? new OperationResult[commands.Count] : null;

        for (var i = 0; i < commands.Count; i++)
        {
            var validation = new ValidationContext();
            await descriptor.ValidateAsync(_services, commands[i], validation, cancellationToken).ConfigureAwait(false);

            OperationFailure? validationFailure = validation.ToFailureResult();
            if (validationFailure is not null)
            {
                context.Result = validationFailure;
                return;
            }

            OperationResult itemResult = await descriptor.InvokeAsync(_services, commands[i], cancellationToken).ConfigureAwait(false);
            if (itemResult.IsFailure)
            {
                context.Result = itemResult;
                return;
            }

            perItem?[i] = itemResult;
        }

        context.BatchResults = perItem;
        context.Result = OperationResult.Ok();
    }
}
