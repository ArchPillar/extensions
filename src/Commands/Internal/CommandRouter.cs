using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Pipelines;
using ArchPillar.Extensions.Operations;

namespace ArchPillar.Extensions.Commands.Internal;

/// <summary>
/// Terminal handler for the command pipeline. Resolves the
/// <see cref="CommandInvokerDescriptor"/> for the dispatched command type,
/// runs the handler's validation, and — if validation passes — invokes the
/// handler. Both calls happen at the innermost point of the pipeline so any
/// user-supplied wrapping middleware (transactions, unit-of-work, retry,
/// distributed locks) is in scope for both validation and execution.
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

    private async Task HandleBatchAsync(
        CommandInvokerDescriptor descriptor,
        CommandContext context,
        IReadOnlyList<IRequest> commands,
        CancellationToken cancellationToken)
    {
        // Batches are all-or-nothing at the validation gate. Validate every
        // command first; if any fails, the whole batch is rejected before
        // the handler is reached. Items that would otherwise have passed
        // get a "batch_rejected" precondition failure so callers can tell
        // them apart from items that failed their own validation.
        //
        // The single outer pipeline pass means wrapping middleware
        // (transactions, retry) covers the whole batch. With no partial
        // processing, a transaction middleware never has to decide
        // mid-batch — it commits the lot or rolls back the lot.
        var perItemValidation = new OperationFailure?[commands.Count];
        var anyValidationFailed = false;

        for (var i = 0; i < commands.Count; i++)
        {
            var validation = new ValidationContext();
            await descriptor.ValidateAsync(_services, commands[i], validation, cancellationToken).ConfigureAwait(false);

            OperationFailure? failure = validation.ToFailureResult();
            if (failure is not null)
            {
                perItemValidation[i] = failure;
                anyValidationFailed = true;
            }
        }

        var final = new OperationResult[commands.Count];

        if (anyValidationFailed)
        {
            for (var i = 0; i < commands.Count; i++)
            {
                final[i] = perItemValidation[i] ?? OperationResult.Failure(
                    OperationStatus.PreconditionFailed,
                    "batch_rejected",
                    OperationStatus.PreconditionFailed.Title(),
                    "Batch rejected because one or more commands failed validation.");
            }

            context.BatchResults = final;
            context.Result = OperationResult.BadRequest(
                "Batch rejected: one or more commands failed validation.",
                type: "batch_rejected");
            return;
        }

        IReadOnlyList<OperationResult> handled;
        if (descriptor.InvokeBatchAsync is { } batch)
        {
            handled = await batch(_services, commands, cancellationToken).ConfigureAwait(false);
            if (handled.Count != commands.Count)
            {
                throw new InvalidOperationException(
                    $"Batch handler for {context.CommandType.FullName} returned {handled.Count} results for {commands.Count} commands.");
            }
        }
        else
        {
            // No batch handler — invoke per-command inside the same outer
            // pipeline pass so wrapping middleware (transaction, retry)
            // covers the whole batch.
            var perItem = new OperationResult[commands.Count];
            for (var i = 0; i < commands.Count; i++)
            {
                perItem[i] = await descriptor.InvokeAsync(_services, commands[i], cancellationToken).ConfigureAwait(false);
            }

            handled = perItem;
        }

        var anyHandlerFailed = false;
        for (var i = 0; i < commands.Count; i++)
        {
            final[i] = handled[i];
            if (handled[i].IsFailure)
            {
                anyHandlerFailed = true;
            }
        }

        context.BatchResults = final;
        context.Result = anyHandlerFailed
            ? OperationResult.BadRequest("One or more batch commands failed during handling.", type: "batch_failed")
            : OperationResult.Ok();
    }
}
