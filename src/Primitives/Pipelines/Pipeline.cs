namespace ArchPillar.Extensions.Primitives;

/// <summary>
/// An async middleware pipeline built from a sequence of
/// <see cref="IPipelineMiddleware{T}"/> steps wrapping an
/// <see cref="IPipelineHandler{T}"/> terminal step.
/// <para>
/// The pipeline is composed as nested lambdas: each middleware wraps the
/// remainder of the pipeline, with the handler at the innermost point.
/// Middlewares that do not call <c>next(...)</c> short-circuit the chain.
/// </para>
/// <para>
/// The delegate chain is pre-built <strong>once</strong> in the constructor,
/// so a single <see cref="Pipeline{T}"/> instance can be reused across many
/// invocations. The hot path through <see cref="ExecuteAsync"/> is a single
/// delegate invocation — no per-call closure allocations — which keeps the
/// pipeline overhead on the nanosecond scale.
/// </para>
/// <para>
/// The constructor accepts plain <see cref="IEnumerable{T}"/> collaborators,
/// making the type trivially resolvable from any DI container. See
/// <c>ArchPillar.Extensions.Primitives.DependencyInjection</c> for the
/// <c>AddPipeline&lt;T&gt;()</c> fluent registration helper, which registers
/// each middleware by its concrete type so pipelines stay isolated from the
/// global <see cref="IPipelineMiddleware{T}"/> service namespace.
/// </para>
/// </summary>
/// <typeparam name="T">The context type passed through the pipeline.</typeparam>
public sealed class Pipeline<T>
{
    private readonly PipelineDelegate<T> _entryPoint;

    /// <summary>
    /// Creates a new pipeline from the supplied handler and the middlewares
    /// enumeration. Middlewares execute in the order yielded by the
    /// enumeration. The delegate chain composing them is built eagerly in
    /// this constructor.
    /// </summary>
    /// <param name="handler">The terminal handler executed after all middlewares.</param>
    /// <param name="middlewares">The middlewares that wrap the handler, in execution order.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="handler"/> or <paramref name="middlewares"/> is <c>null</c>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown if any entry in <paramref name="middlewares"/> is <c>null</c>.
    /// </exception>
    public Pipeline(IPipelineHandler<T> handler, IEnumerable<IPipelineMiddleware<T>> middlewares)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(middlewares);

        IPipelineMiddleware<T>[] snapshot = [.. middlewares];

        for (var i = 0; i < snapshot.Length; i++)
        {
            if (snapshot[i] is null)
            {
                throw new ArgumentException(
                    $"Middleware at index {i} is null.",
                    nameof(middlewares));
            }
        }

        MiddlewareCount = snapshot.Length;

        // Pre-build the nested delegate chain once. Each layer captures its
        // middleware and the pre-built "downstream" delegate. Invocation is
        // a single delegate call with no per-call closure allocation.
        PipelineDelegate<T> chain = handler.HandleAsync;
        for (var i = snapshot.Length - 1; i >= 0; i--)
        {
            IPipelineMiddleware<T> middleware = snapshot[i];
            PipelineDelegate<T> next = chain;
            chain = (context, cancellationToken) => middleware.InvokeAsync(context, next, cancellationToken);
        }

        _entryPoint = chain;
    }

    /// <summary>
    /// The number of middlewares registered in this pipeline, not counting
    /// the terminal handler.
    /// </summary>
    public int MiddlewareCount { get; }

    /// <summary>
    /// Executes the pipeline with the supplied context.
    /// <para>
    /// Each middleware runs in registration order; those that call
    /// <c>next(...)</c> allow the remainder of the pipeline (including the
    /// handler) to run, while those that return without calling <c>next(...)</c>
    /// short-circuit the chain.
    /// </para>
    /// </summary>
    /// <param name="context">The context flowing through the pipeline.</param>
    /// <param name="cancellationToken">The cancellation token for the execution.</param>
    /// <returns>A task that completes when the pipeline has finished.</returns>
    public Task ExecuteAsync(T context, CancellationToken cancellationToken = default)
        => _entryPoint(context, cancellationToken);
}
