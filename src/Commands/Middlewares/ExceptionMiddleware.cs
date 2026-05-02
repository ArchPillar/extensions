using ArchPillar.Extensions.Pipelines;
using ArchPillar.Extensions.Primitives;

namespace ArchPillar.Extensions.Commands.Middlewares;

/// <summary>
/// Catches exceptions thrown from downstream middleware and the handler.
/// <see cref="OperationException"/> is unwrapped back into its carried
/// <see cref="OperationResult"/>; any other exception becomes
/// <see cref="OperationStatus.InternalServerError"/>.
/// </summary>
/// <remarks>
/// Cancellation (<see cref="OperationCanceledException"/>) is left to
/// propagate — it is not an application-level outcome.
/// </remarks>
public sealed class ExceptionMiddleware : IPipelineMiddleware<CommandContext>
{
    /// <inheritdoc/>
    public async Task InvokeAsync(
        CommandContext context,
        PipelineDelegate<CommandContext> next,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        try
        {
            await next(context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationException ex)
        {
            context.Result = ex.Result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            context.Result = OperationResult.Failed(ex);
        }
    }
}
