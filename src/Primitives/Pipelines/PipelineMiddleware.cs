namespace ArchPillar.Extensions.Primitives;

/// <summary>
/// Factory helpers for creating <see cref="IPipelineMiddleware{T}"/> instances
/// from plain delegates. Useful for tests and small in-place pipelines where
/// defining a class is overkill.
/// </summary>
public static class PipelineMiddleware
{
    /// <summary>
    /// Wraps an async delegate as an <see cref="IPipelineMiddleware{T}"/>.
    /// </summary>
    /// <param name="invoke">
    /// A delegate invoked with the context, a <c>next</c> continuation, and
    /// a cancellation token. Call <c>next(ctx, ct)</c> to continue the
    /// pipeline; skip it to short-circuit.
    /// </param>
    /// <typeparam name="T">The context type.</typeparam>
    /// <returns>An <see cref="IPipelineMiddleware{T}"/> that invokes <paramref name="invoke"/>.</returns>
    public static IPipelineMiddleware<T> FromDelegate<T>(Func<T, PipelineDelegate<T>, CancellationToken, Task> invoke)
    {
        ArgumentNullException.ThrowIfNull(invoke);
        return new DelegateMiddleware<T>(invoke);
    }

    /// <summary>
    /// Wraps an async delegate as an <see cref="IPipelineMiddleware{T}"/>.
    /// </summary>
    /// <param name="invoke">
    /// A delegate invoked with the context and a <c>next</c> continuation.
    /// Cancellation is not observed — pass the default token when calling
    /// <c>next</c> or use the overload that exposes it.
    /// </param>
    /// <typeparam name="T">The context type.</typeparam>
    /// <returns>An <see cref="IPipelineMiddleware{T}"/> that invokes <paramref name="invoke"/>.</returns>
    public static IPipelineMiddleware<T> FromDelegate<T>(Func<T, PipelineDelegate<T>, Task> invoke)
    {
        ArgumentNullException.ThrowIfNull(invoke);
        return new DelegateMiddleware<T>((ctx, next, _) => invoke(ctx, next));
    }

    private sealed class DelegateMiddleware<T>(Func<T, PipelineDelegate<T>, CancellationToken, Task> invoke) : IPipelineMiddleware<T>
    {
        public Task InvokeAsync(T context, PipelineDelegate<T> next, CancellationToken cancellationToken = default)
            => invoke(context, next, cancellationToken);
    }
}
