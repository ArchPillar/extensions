using ArchPillar.Extensions.Pipelines;
using ArchPillar.Extensions.Primitives;

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

    public async Task HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        CommandInvokerDescriptor descriptor = _registry.Get(context.CommandType);

        // Validation runs inside whatever wrapping the user has placed around
        // the router (typically a transaction). This keeps entity-existence
        // and entity-state checks consistent with the snapshot the handler
        // writes against — no TOCTOU between validation read and handler
        // write.
        await descriptor.ValidateAsync(_services, context.Command, context.Validation, cancellationToken).ConfigureAwait(false);
        if (context.Validation.HasErrors)
        {
            context.Result = OperationResult.ValidationFailed(context.Validation.Errors);
            return;
        }

        OperationResult result = await descriptor.InvokeAsync(_services, context.Command, cancellationToken).ConfigureAwait(false);
        context.Result = result;
    }
}
