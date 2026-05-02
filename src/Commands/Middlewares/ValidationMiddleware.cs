using ArchPillar.Extensions.Commands.Internal;
using ArchPillar.Extensions.Pipelines;
using ArchPillar.Extensions.Primitives;

namespace ArchPillar.Extensions.Commands.Middlewares;

/// <summary>
/// Calls the registered handler's <c>ValidateAsync</c> via the router
/// descriptor and short-circuits the pipeline with
/// <see cref="OperationStatus.UnprocessableEntity"/> if any errors were
/// accumulated.
/// </summary>
public sealed class ValidationMiddleware : IPipelineMiddleware<CommandContext>
{
    private readonly CommandInvokerRegistry _registry;
    private readonly IServiceProvider _services;

    /// <summary>
    /// Initializes a new <see cref="ValidationMiddleware"/>.
    /// </summary>
    /// <param name="registry">The command invoker registry.</param>
    /// <param name="services">The active service provider (scoped if available).</param>
    public ValidationMiddleware(CommandInvokerRegistry registry, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(services);
        _registry = registry;
        _services = services;
    }

    /// <inheritdoc/>
    public async Task InvokeAsync(
        CommandContext context,
        PipelineDelegate<CommandContext> next,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (!_registry.TryGet(context.CommandType, out CommandInvokerDescriptor descriptor))
        {
            // No handler registered — let the router downstream produce the
            // canonical failure. Validation has nothing to call.
            await next(context, cancellationToken).ConfigureAwait(false);
            return;
        }

        await descriptor.ValidateAsync(_services, context.Command, context.Validation, cancellationToken).ConfigureAwait(false);

        if (context.Validation.HasErrors)
        {
            context.Result = OperationResult.ValidationFailed(context.Validation.Errors);
            return;
        }

        await next(context, cancellationToken).ConfigureAwait(false);
    }
}
