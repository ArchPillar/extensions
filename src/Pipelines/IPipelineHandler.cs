namespace ArchPillar.Extensions.Pipelines;

/// <summary>
/// The terminal step of a <see cref="Pipeline{T}"/> — executed after every
/// middleware in the chain has called <c>next()</c>. Each pipeline has exactly
/// one handler, and it runs last (the innermost lambda in the nested chain).
/// </summary>
/// <remarks>
/// Implementations are expected to be resolvable from a DI container, so that
/// constructor-injected dependencies (loggers, repositories, etc.) can be
/// supplied by the host.
/// </remarks>
/// <typeparam name="T">The context type passed through the pipeline.</typeparam>
public interface IPipelineHandler<in T>
{
    /// <summary>
    /// Executes the terminal step of the pipeline.
    /// </summary>
    /// <param name="context">The context flowing through the pipeline.</param>
    /// <param name="cancellationToken">The cancellation token for the execution.</param>
    /// <returns>A task that completes when the handler is done.</returns>
    public Task HandleAsync(T context, CancellationToken cancellationToken = default);
}
