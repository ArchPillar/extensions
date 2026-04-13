using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArchPillar.Extensions.Pipelines;

/// <summary>
/// Fluent builder returned by
/// <see cref="ServiceCollectionExtensions.AddPipeline{T}(IServiceCollection, ServiceLifetime)"/>.
/// Registers middlewares and a terminal handler for a <see cref="Pipeline{T}"/>
/// <strong>directly in the service collection</strong> — the builder itself
/// holds no state.
/// </summary>
/// <remarks>
/// <para>
/// Every call writes a <see cref="ServiceDescriptor"/> into the
/// <see cref="IServiceCollection"/>:
/// </para>
/// <list type="bullet">
///   <item>
///     <see cref="Use{TMiddleware}"/> uses
///     <c>TryAddEnumerable(IPipelineMiddleware&lt;T&gt;, TMiddleware)</c>, so calling
///     it twice with the same middleware is a no-op. Middlewares execute in the
///     order they were added — the order in which <see cref="IEnumerable{T}"/>
///     services come out of the container matches their registration order.
///   </item>
///   <item>
///     <see cref="Handle{THandler}"/> uses
///     <c>TryAdd(IPipelineHandler&lt;T&gt;, THandler)</c>, so the first handler
///     configured for a given context type wins.
///   </item>
/// </list>
/// <para>
/// When <see cref="Pipeline{T}"/> is resolved from the container, its public
/// constructor is invoked with the registered handler and the full
/// <see cref="IEnumerable{T}"/> of registered middlewares — proper DI
/// composition, no captured closure state.
/// </para>
/// </remarks>
/// <typeparam name="T">The context type the pipeline will process.</typeparam>
public sealed class PipelineServiceBuilder<T>
{
    private readonly IServiceCollection _services;

    internal PipelineServiceBuilder(IServiceCollection services, ServiceLifetime lifetime)
    {
        _services = services;
        Lifetime = lifetime;
    }

    /// <summary>
    /// The DI lifetime applied to the <see cref="Pipeline{T}"/>, the handler,
    /// and every middleware registered through this builder.
    /// </summary>
    public ServiceLifetime Lifetime { get; }

    /// <summary>
    /// Appends a middleware class to the pipeline. The middleware is registered
    /// as <see cref="IPipelineMiddleware{T}"/> via
    /// <see cref="ServiceCollectionDescriptorExtensions.TryAddEnumerable(IServiceCollection, ServiceDescriptor)"/>
    /// so calling this method twice for the same middleware type is a no-op.
    /// Middlewares execute in the order they were added.
    /// </summary>
    /// <typeparam name="TMiddleware">The middleware class.</typeparam>
    /// <returns>This builder, for chaining.</returns>
    public PipelineServiceBuilder<T> Use<TMiddleware>()
        where TMiddleware : class, IPipelineMiddleware<T>
    {
        _services.TryAddEnumerable(ServiceDescriptor.Describe(
            typeof(IPipelineMiddleware<T>),
            typeof(TMiddleware),
            Lifetime));
        return this;
    }

    /// <summary>
    /// Configures the terminal handler class for the pipeline. The handler is
    /// registered as <see cref="IPipelineHandler{T}"/> via
    /// <see cref="ServiceCollectionDescriptorExtensions.TryAdd(IServiceCollection, ServiceDescriptor)"/>
    /// so the first handler configured for a given context type wins — calling
    /// this method a second time is silently ignored.
    /// </summary>
    /// <typeparam name="THandler">The handler class.</typeparam>
    /// <returns>This builder, for chaining.</returns>
    public PipelineServiceBuilder<T> Handle<THandler>()
        where THandler : class, IPipelineHandler<T>
    {
        _services.TryAdd(ServiceDescriptor.Describe(
            typeof(IPipelineHandler<T>),
            typeof(THandler),
            Lifetime));
        return this;
    }
}
