namespace ArchPillar.Extensions.Pipelines;

/// <summary>
/// Delegate representing the rest of a pipeline as a single invokable chain.
/// Given a context and a cancellation token it runs every remaining
/// middleware and (if no short-circuit occurs) the terminal handler.
/// </summary>
/// <remarks>
/// <para>
/// This delegate is the <c>next</c> parameter passed into every
/// <see cref="IPipelineMiddleware{T}"/>. The chain is pre-built once by
/// <see cref="Pipeline{T}"/> in its constructor, so passing control to
/// <c>next</c> is a simple delegate invocation with no per-call allocation.
/// </para>
/// <para>
/// Middlewares call this as <c>await next(context, cancellationToken)</c> —
/// the context and cancellation token flow explicitly through every layer.
/// </para>
/// </remarks>
/// <typeparam name="T">The context type passed through the pipeline.</typeparam>
/// <param name="context">The context flowing through the pipeline.</param>
/// <param name="cancellationToken">The cancellation token for the execution.</param>
/// <returns>A task that completes when the remainder of the pipeline is done.</returns>
public delegate Task PipelineDelegate<in T>(T context, CancellationToken cancellationToken);
