namespace ArchPillar.Extensions.Primitives;

/// <summary>
/// Fluent builder for a <see cref="Pipeline{T}"/>. Accepts middlewares and a
/// terminal handler as concrete instances (or as plain delegates) and
/// produces a compiled pipeline via <see cref="Build"/>.
/// </summary>
/// <remarks>
/// Use this builder when you are wiring pipelines by hand (tests, console
/// apps, or any scenario without a DI container). For DI-hosted applications,
/// see <c>services.AddPipeline&lt;T&gt;()</c> in
/// <c>ArchPillar.Extensions.Primitives.DependencyInjection</c>.
/// </remarks>
/// <typeparam name="T">The context type the pipeline will process.</typeparam>
public sealed class PipelineBuilder<T>
{
    private readonly List<IPipelineMiddleware<T>> _middlewares = [];
    private IPipelineHandler<T>? _handler;

    /// <summary>
    /// Appends a middleware to the pipeline. Middlewares execute in the order
    /// they were added.
    /// </summary>
    /// <param name="middleware">The middleware instance to append.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="middleware"/> is <c>null</c>.</exception>
    public PipelineBuilder<T> Use(IPipelineMiddleware<T> middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        _middlewares.Add(middleware);
        return this;
    }

    /// <summary>
    /// Appends a delegate-based middleware to the pipeline. Equivalent to
    /// <c>Use(PipelineMiddleware.FromDelegate(invoke))</c>.
    /// </summary>
    /// <param name="invoke">The middleware delegate.</param>
    /// <returns>This builder, for chaining.</returns>
    public PipelineBuilder<T> Use(Func<T, PipelineDelegate<T>, CancellationToken, Task> invoke)
        => Use(PipelineMiddleware.FromDelegate(invoke));

    /// <summary>
    /// Appends a delegate-based middleware to the pipeline. Equivalent to
    /// <c>Use(PipelineMiddleware.FromDelegate(invoke))</c>.
    /// </summary>
    /// <param name="invoke">The middleware delegate.</param>
    /// <returns>This builder, for chaining.</returns>
    public PipelineBuilder<T> Use(Func<T, PipelineDelegate<T>, Task> invoke)
        => Use(PipelineMiddleware.FromDelegate(invoke));

    /// <summary>
    /// Sets the terminal handler for this pipeline. Calling this replaces any
    /// previously configured handler.
    /// </summary>
    /// <param name="handler">The handler instance.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="handler"/> is <c>null</c>.</exception>
    public PipelineBuilder<T> Handle(IPipelineHandler<T> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
        return this;
    }

    /// <summary>
    /// Sets a delegate-based terminal handler. Equivalent to
    /// <c>Handle(PipelineHandler.FromDelegate(handle))</c>.
    /// </summary>
    /// <param name="handle">The handler delegate.</param>
    /// <returns>This builder, for chaining.</returns>
    public PipelineBuilder<T> Handle(Func<T, CancellationToken, Task> handle)
        => Handle(PipelineHandler.FromDelegate(handle));

    /// <summary>
    /// Sets a delegate-based terminal handler. Equivalent to
    /// <c>Handle(PipelineHandler.FromDelegate(handle))</c>.
    /// </summary>
    /// <param name="handle">The handler delegate.</param>
    /// <returns>This builder, for chaining.</returns>
    public PipelineBuilder<T> Handle(Func<T, Task> handle)
        => Handle(PipelineHandler.FromDelegate(handle));

    /// <summary>
    /// Sets a delegate-based terminal handler. Equivalent to
    /// <c>Handle(PipelineHandler.FromDelegate(handle))</c>.
    /// </summary>
    /// <param name="handle">The handler delegate.</param>
    /// <returns>This builder, for chaining.</returns>
    public PipelineBuilder<T> Handle(Action<T> handle)
        => Handle(PipelineHandler.FromDelegate(handle));

    /// <summary>
    /// Compiles the configured middlewares and handler into a
    /// <see cref="Pipeline{T}"/>.
    /// </summary>
    /// <returns>A ready-to-execute pipeline.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if no terminal handler has been configured via <see cref="Handle(IPipelineHandler{T})"/>
    /// or one of its overloads.
    /// </exception>
    public Pipeline<T> Build()
    {
        if (_handler is null)
        {
            throw new InvalidOperationException(
                $"No handler configured for Pipeline<{typeof(T).Name}>. " +
                $"Call Handle(...) before Build().");
        }

        return new Pipeline<T>(_handler, _middlewares);
    }
}
