using System.Diagnostics;

namespace ArchPillar.Extensions.Pipelines;

/// <summary>
/// An <see cref="IPipelineMiddleware{T}"/> that starts a
/// <see cref="Activity"/> around the downstream pipeline, driven by the
/// <see cref="IPipelineContext"/> implemented on <typeparamref name="T"/>.
/// </summary>
/// <remarks>
/// <para>
/// The middleware uses the library-owned <see cref="ActivitySource"/> named
/// <see cref="PipelineActivitySource.Name"/>. When no
/// <see cref="ActivityListener"/> is subscribed,
/// <see cref="ActivitySource.StartActivity(string, ActivityKind, ActivityContext, System.Collections.Generic.IEnumerable{System.Collections.Generic.KeyValuePair{string, object}}, System.Collections.Generic.IEnumerable{ActivityLink}, System.DateTimeOffset)"/>
/// returns <c>null</c> and the middleware is a pass-through.
/// </para>
/// <para>
/// On exception from the downstream chain, the middleware records an
/// <c>exception</c> event on the activity with OpenTelemetry-conventional
/// tags (<c>exception.type</c>, <c>exception.message</c>,
/// <c>exception.stacktrace</c>, <c>exception.escaped</c>) and sets the
/// activity status to <see cref="ActivityStatusCode.Error"/>. The exception
/// then propagates to callers — the middleware never swallows.
/// </para>
/// </remarks>
/// <typeparam name="T">
/// The pipeline context type. Must be a reference type that implements
/// <see cref="IPipelineContext"/>.
/// </typeparam>
public sealed class ActivityMiddleware<T> : IPipelineMiddleware<T>
    where T : class, IPipelineContext
{
    /// <inheritdoc/>
    public Task InvokeAsync(T context, PipelineDelegate<T> next, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        Activity? activity = PipelineActivitySource.Instance.StartActivity(
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
        T context,
        PipelineDelegate<T> next,
        CancellationToken cancellationToken)
    {
        using (activity)
        {
            try
            {
                context.EnrichActivity(activity);

                await next(context, cancellationToken);
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
