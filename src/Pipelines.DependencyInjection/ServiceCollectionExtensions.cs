using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArchPillar.Extensions.Pipelines;

/// <summary>
/// <see cref="IServiceCollection"/> extensions for registering a
/// <see cref="Pipeline{T}"/> along with its middlewares and terminal handler.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <see cref="Pipeline{T}"/> in the service collection and
    /// returns a fluent <see cref="PipelineServiceBuilder{T}"/> for configuring
    /// its middlewares and terminal handler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The registration is <strong>forgiving</strong>: both this method and
    /// the builder's <c>Use&lt;&gt;</c>/<c>Handle&lt;&gt;</c> methods use
    /// <see cref="ServiceCollectionDescriptorExtensions.TryAdd(IServiceCollection, ServiceDescriptor)"/>
    /// and
    /// <see cref="ServiceCollectionDescriptorExtensions.TryAddEnumerable(IServiceCollection, ServiceDescriptor)"/>.
    /// Calling <c>AddPipeline&lt;T&gt;()</c> a second time for the same context
    /// type is a no-op; calling <c>Use&lt;M&gt;()</c> twice for the same
    /// middleware type is a no-op; calling <c>Handle&lt;H&gt;()</c> a second
    /// time is silently ignored (first handler wins). This means multiple
    /// modules can each contribute middlewares to the same pipeline without
    /// having to coordinate ordering or detect existing registrations.
    /// </para>
    /// <para>
    /// The <see cref="Pipeline{T}"/> itself is registered as a self-constructed
    /// service — when it is resolved, DI invokes its public constructor with
    /// the registered <see cref="IPipelineHandler{T}"/> and the full
    /// <see cref="IEnumerable{T}"/> of registered
    /// <see cref="IPipelineMiddleware{T}"/> services. This is standard DI
    /// composition: middlewares and handlers are real container services, not
    /// closure-captured state.
    /// </para>
    /// <code>
    /// services.AddPipeline&lt;OrderContext&gt;()
    ///     .Use&lt;LoggingMiddleware&gt;()
    ///     .Use&lt;ValidationMiddleware&gt;()
    ///     .Use&lt;TransactionMiddleware&gt;()
    ///     .Handle&lt;PlaceOrderHandler&gt;();
    /// </code>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="lifetime">The lifetime applied to the pipeline and its steps. Defaults to <see cref="ServiceLifetime.Scoped"/>.</param>
    /// <typeparam name="T">The context type the pipeline will process.</typeparam>
    /// <returns>A fluent builder for configuring the pipeline.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> is <c>null</c>.</exception>
    public static PipelineServiceBuilder<T> AddPipeline<T>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register Pipeline<T> as a self-constructed service. On resolve, DI
        // will invoke Pipeline<T>'s public constructor with the registered
        // IPipelineHandler<T> and IEnumerable<IPipelineMiddleware<T>>.
        services.TryAdd(ServiceDescriptor.Describe(
            typeof(Pipeline<T>),
            typeof(Pipeline<T>),
            lifetime));

        return new PipelineServiceBuilder<T>(services, lifetime);
    }
}
