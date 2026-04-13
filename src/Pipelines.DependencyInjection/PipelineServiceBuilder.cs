using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArchPillar.Extensions.Pipelines;

/// <summary>
/// Fluent builder returned by
/// <see cref="ServiceCollectionExtensions.AddPipeline{T}(IServiceCollection, ServiceLifetime)"/>.
/// Registers middlewares and a terminal handler for a <see cref="Pipeline{T}"/>,
/// and produces the pipeline at resolve time.
/// </summary>
/// <remarks>
/// <para>
/// Middlewares are registered by their concrete type — not as global
/// <see cref="IPipelineMiddleware{T}"/> services — so that two pipelines for
/// the same context type, or unrelated middlewares registered elsewhere, do
/// not get conflated. At resolve time, the builder walks its own list of
/// registered types in order and asks the container for each one, giving
/// every middleware the full benefit of constructor injection without
/// polluting the <see cref="IPipelineMiddleware{T}"/> service namespace.
/// </para>
/// </remarks>
/// <typeparam name="T">The context type the pipeline will process.</typeparam>
public sealed class PipelineServiceBuilder<T>
{
    private readonly IServiceCollection _services;
    private readonly List<Type> _middlewareTypes = [];
    private Type? _handlerType;

    internal PipelineServiceBuilder(IServiceCollection services, ServiceLifetime lifetime)
    {
        _services = services;
        Lifetime = lifetime;
    }

    /// <summary>
    /// The DI lifetime applied to the <see cref="Pipeline{T}"/> and any
    /// middleware/handler classes registered through this builder.
    /// </summary>
    public ServiceLifetime Lifetime { get; }

    /// <summary>
    /// The number of middlewares registered so far. The terminal handler is
    /// not counted.
    /// </summary>
    public int MiddlewareCount => _middlewareTypes.Count;

    /// <summary>
    /// Appends a middleware class to the pipeline. The class is registered
    /// in the service collection (by its own concrete type) if it isn't
    /// already, so its own constructor dependencies are resolved from DI.
    /// Middlewares execute in the order they were added.
    /// </summary>
    /// <typeparam name="TMiddleware">The middleware class.</typeparam>
    /// <returns>This builder, for chaining.</returns>
    public PipelineServiceBuilder<T> Use<TMiddleware>()
        where TMiddleware : class, IPipelineMiddleware<T>
    {
        _services.TryAdd(new ServiceDescriptor(typeof(TMiddleware), typeof(TMiddleware), Lifetime));
        _middlewareTypes.Add(typeof(TMiddleware));
        return this;
    }

    /// <summary>
    /// Sets the terminal handler class for the pipeline. The class is
    /// registered in the service collection (by its own concrete type) if it
    /// isn't already, so its own constructor dependencies are resolved from DI.
    /// </summary>
    /// <typeparam name="THandler">The handler class.</typeparam>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if a handler has already been registered for this pipeline.
    /// A pipeline has exactly one terminal handler.
    /// </exception>
    public PipelineServiceBuilder<T> Handle<THandler>()
        where THandler : class, IPipelineHandler<T>
    {
        if (_handlerType is not null)
        {
            throw new InvalidOperationException(
                $"A handler for Pipeline<{typeof(T).Name}> is already registered ({_handlerType.Name}). " +
                "A pipeline has exactly one terminal handler.");
        }

        _services.TryAdd(new ServiceDescriptor(typeof(THandler), typeof(THandler), Lifetime));
        _handlerType = typeof(THandler);
        return this;
    }

    /// <summary>
    /// Factory used by the <see cref="Pipeline{T}"/> service descriptor to
    /// materialize a pipeline from the configured handler and middleware types.
    /// </summary>
    internal Pipeline<T> BuildPipeline(IServiceProvider serviceProvider)
    {
        if (_handlerType is null)
        {
            throw new InvalidOperationException(
                $"Pipeline<{typeof(T).Name}> has no terminal handler. " +
                "Call .Handle<THandler>() when configuring the pipeline.");
        }

        var handler = (IPipelineHandler<T>)serviceProvider.GetRequiredService(_handlerType);

        var middlewares = new IPipelineMiddleware<T>[_middlewareTypes.Count];
        for (var i = 0; i < _middlewareTypes.Count; i++)
        {
            middlewares[i] = (IPipelineMiddleware<T>)serviceProvider.GetRequiredService(_middlewareTypes[i]);
        }

        return new Pipeline<T>(handler, middlewares);
    }
}
