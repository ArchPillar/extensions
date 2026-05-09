using System.Diagnostics;
using ArchPillar.Extensions.Pipelines;

namespace ArchPillar.Extensions.Commands.Middlewares;

/// <summary>
/// Starts an <see cref="Activity"/> on the Commands-specific
/// <see cref="CommandActivitySource"/> for every dispatch. Subscribers
/// (OpenTelemetry, raw <see cref="ActivityListener"/>) reference
/// <see cref="CommandActivitySource.Name"/> to opt in.
/// </summary>
/// <remarks>
/// Mirrors the design of <see cref="ActivityMiddleware{T}"/> from
/// ArchPillar.Extensions.Pipelines but uses the Commands-owned
/// <see cref="ActivitySource"/> so that subscribers can listen to command
/// dispatches without also receiving every other pipeline's activities.
/// </remarks>
public sealed class CommandActivityMiddleware : IPipelineMiddleware<CommandContext>
{
    /// <inheritdoc/>
    public Task InvokeAsync(
        CommandContext context,
        PipelineDelegate<CommandContext> next,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        Activity? activity = CommandActivitySource.Instance.StartActivity(
            context.OperationName,
            context.ActivityKind,
            context.ParentContext);

        if (activity is null)
        {
            return next(context, cancellationToken);
        }

        return InvokeWithActivityAsync(activity, context, next, cancellationToken);
    }

    private static async Task InvokeWithActivityAsync(
        Activity activity,
        CommandContext context,
        PipelineDelegate<CommandContext> next,
        CancellationToken cancellationToken)
    {
        using (activity)
        {
            try
            {
                context.EnrichActivity(activity);

                await next(context, cancellationToken).ConfigureAwait(false);

                // ExceptionMiddleware (registered inside this one) absorbs
                // OperationException / unhandled throws into context.Result.
                // From this middleware's perspective the call returned
                // normally — so we have to consult the result slot to know
                // whether the dispatch actually succeeded.
                if (context.Result is { IsFailure: true } failure)
                {
                    activity.SetTag("command.status", (int)failure.Status);
                    activity.SetStatus(
                        ActivityStatusCode.Error,
                        failure.Problem?.Detail ?? failure.Status.ToString());
                }
            }
            catch (Exception ex)
            {
                activity.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
                {
                    { "exception.type", ex.GetType().FullName },
                    { "exception.message", ex.Message },
                    { "exception.stacktrace", ex.StackTrace },
                    { "exception.escaped", true },
                }));
                activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                throw;
            }
        }
    }
}
