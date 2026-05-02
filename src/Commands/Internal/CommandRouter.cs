using ArchPillar.Extensions.Pipelines;
using ArchPillar.Extensions.Primitives;

namespace ArchPillar.Extensions.Commands.Internal;

/// <summary>
/// Terminal handler for the command pipeline. Resolves the
/// <see cref="CommandInvokerDescriptor"/> for the dispatched command type and
/// invokes its <c>InvokeAsync</c>, writing the resulting
/// <see cref="OperationResult"/> back to the context.
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
        OperationResult result = await descriptor.InvokeAsync(_services, context.Command, cancellationToken).ConfigureAwait(false);
        context.Result = result;
    }
}
