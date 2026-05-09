namespace ArchPillar.Extensions.Pipelines;

/// <summary>
/// Factory helpers for creating <see cref="IPipelineHandler{T}"/> instances
/// from plain delegates. Useful for tests and small in-place pipelines where
/// defining a class is overkill.
/// </summary>
public static class PipelineHandler
{
    /// <summary>
    /// Wraps an async delegate as an <see cref="IPipelineHandler{T}"/>.
    /// </summary>
    /// <param name="handle">
    /// A delegate invoked with the context and a cancellation token. The
    /// returned task must complete when the handler is done.
    /// </param>
    /// <typeparam name="T">The context type.</typeparam>
    /// <returns>An <see cref="IPipelineHandler{T}"/> that invokes <paramref name="handle"/>.</returns>
    public static IPipelineHandler<T> FromDelegate<T>(Func<T, CancellationToken, Task> handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        return new DelegateHandler<T>(handle);
    }

    /// <summary>
    /// Wraps an async delegate as an <see cref="IPipelineHandler{T}"/>.
    /// </summary>
    /// <param name="handle">A delegate invoked with the context. Cancellation is not observed.</param>
    /// <typeparam name="T">The context type.</typeparam>
    /// <returns>An <see cref="IPipelineHandler{T}"/> that invokes <paramref name="handle"/>.</returns>
    public static IPipelineHandler<T> FromDelegate<T>(Func<T, Task> handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        return new DelegateHandler<T>((context, _) => handle(context));
    }

    /// <summary>
    /// Wraps a synchronous delegate as an <see cref="IPipelineHandler{T}"/>.
    /// The returned handler completes synchronously via
    /// <see cref="Task.CompletedTask"/>.
    /// </summary>
    /// <param name="handle">A delegate invoked with the context.</param>
    /// <typeparam name="T">The context type.</typeparam>
    /// <returns>An <see cref="IPipelineHandler{T}"/> that invokes <paramref name="handle"/>.</returns>
    public static IPipelineHandler<T> FromDelegate<T>(Action<T> handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        return new DelegateHandler<T>((context, _) =>
        {
            handle(context);
            return Task.CompletedTask;
        });
    }

    private sealed class DelegateHandler<T>(Func<T, CancellationToken, Task> handle) : IPipelineHandler<T>
    {
        public Task HandleAsync(T context, CancellationToken cancellationToken = default)
            => handle(context, cancellationToken);
    }
}
