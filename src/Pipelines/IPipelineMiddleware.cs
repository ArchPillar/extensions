namespace ArchPillar.Extensions.Pipelines;

/// <summary>
/// A step that wraps the remainder of a <see cref="Pipeline{T}"/>. Each
/// middleware receives the current context and a <c>next</c> delegate that
/// invokes the rest of the chain. Middlewares can run code before and/or
/// after <c>next</c>, and short-circuit the pipeline by simply not calling it.
/// </summary>
/// <remarks>
/// <para>
/// Implementations are expected to be resolvable from a DI container, so that
/// constructor-injected dependencies can be supplied by the host.
/// </para>
/// <para>
/// The <c>next</c> delegate is a <see cref="PipelineDelegate{T}"/> — a
/// pre-built delegate representing every remaining step of the pipeline.
/// Because the chain is composed once in the <see cref="Pipeline{T}"/>
/// constructor, invoking <c>next(context, cancellationToken)</c> does not
/// allocate on the hot path.
/// </para>
/// <code>
/// public async Task InvokeAsync(TContext context, PipelineDelegate&lt;TContext&gt; next, CancellationToken cancellationToken)
/// {
///     // before
///     await next(context, cancellationToken);
///     // after
/// }
/// </code>
/// </remarks>
/// <typeparam name="T">The context type passed through the pipeline.</typeparam>
public interface IPipelineMiddleware<T>
{
    /// <summary>
    /// Executes this middleware as part of the pipeline chain.
    /// </summary>
    /// <param name="context">The context flowing through the pipeline.</param>
    /// <param name="next">
    /// A delegate that invokes the next step in the pipeline (either another
    /// middleware or the terminal handler). Await it to continue; skip it to
    /// short-circuit.
    /// </param>
    /// <param name="cancellationToken">The cancellation token for the execution.</param>
    /// <returns>A task that completes when this middleware and its downstream steps are done.</returns>
    public Task InvokeAsync(T context, PipelineDelegate<T> next, CancellationToken cancellationToken = default);
}
