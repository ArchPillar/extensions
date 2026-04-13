using Microsoft.Extensions.DependencyInjection;

namespace ArchPillar.Extensions.Primitives;

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
    /// Middlewares are registered by their concrete type, not as the global
    /// <see cref="IPipelineMiddleware{T}"/> service. This keeps every pipeline
    /// isolated: two pipelines for the same context type can have completely
    /// different sets of middlewares, and registering
    /// <see cref="IPipelineMiddleware{T}"/> elsewhere in the container does
    /// not accidentally attach it to the pipeline built here.
    /// </para>
    /// <para>
    /// Call this method exactly once per context type <typeparamref name="T"/>.
    /// Calling it a second time for the same type throws
    /// <see cref="InvalidOperationException"/>.
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
    /// <exception cref="InvalidOperationException">
    /// Thrown if a <see cref="Pipeline{T}"/> has already been registered for this context type.
    /// </exception>
    public static PipelineServiceBuilder<T> AddPipeline<T>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(services);

        for (var i = 0; i < services.Count; i++)
        {
            if (services[i].ServiceType == typeof(Pipeline<T>))
            {
                throw new InvalidOperationException(
                    $"A Pipeline<{typeof(T).Name}> is already registered in this service collection. " +
                    "Call AddPipeline<T>() only once per context type and configure all middlewares " +
                    "and the handler in a single chain.");
            }
        }

        var builder = new PipelineServiceBuilder<T>(services, lifetime);

        services.Add(new ServiceDescriptor(
            typeof(Pipeline<T>),
            builder.BuildPipeline,
            lifetime));

        return builder;
    }
}
