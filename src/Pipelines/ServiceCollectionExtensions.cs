using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArchPillar.Extensions.Pipelines;

/// <summary>
/// <see cref="IServiceCollection"/> extensions for registering a
/// <see cref="Pipeline{T}"/>, its middlewares, and its terminal handler.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline, each middleware, and the handler each have their own
/// <see cref="ServiceLifetime"/>. Cross-registration lifetime validation is
/// left to the container (enable <c>ValidateScopes</c> and
/// <c>ValidateOnBuild</c> on <c>ServiceProviderOptions</c> to catch captive
/// dependencies).
/// </para>
/// <para>
/// For delegate-based handlers, wrap the delegate with
/// <see cref="PipelineHandler.FromDelegate{T}(Func{T, CancellationToken, Task})"/>
/// (or one of its overloads) and pass the result to
/// <see cref="AddPipeline{T}(IServiceCollection, IPipelineHandler{T}, ServiceLifetime)"/>.
/// </para>
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <see cref="Pipeline{T}"/> with a required terminal handler.
    /// </summary>
    /// <remarks>
    /// The pipeline is registered as a self-constructed service — the container
    /// resolves it by invoking its public constructor with the registered
    /// <see cref="IPipelineHandler{T}"/> and the full
    /// <see cref="IEnumerable{T}"/> of registered
    /// <see cref="IPipelineMiddleware{T}"/> services. Register middlewares via
    /// <see cref="AddPipelineMiddleware{T, TMiddleware}"/>. Pipelines without a
    /// handler are not valid — if you don't have a real handler yet, register
    /// a dummy that throws.
    /// </remarks>
    /// <typeparam name="T">The context type.</typeparam>
    /// <typeparam name="THandler">The terminal handler class.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="pipelineLifetime">Lifetime for <see cref="Pipeline{T}"/>. Defaults to <see cref="ServiceLifetime.Scoped"/>.</param>
    /// <param name="handlerLifetime">Lifetime for the handler. Defaults to <paramref name="pipelineLifetime"/>.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown if <paramref name="pipelineLifetime"/> is <see cref="ServiceLifetime.Singleton"/>
    /// and the resolved handler lifetime is <see cref="ServiceLifetime.Scoped"/>.
    /// </exception>
    public static IServiceCollection AddPipeline<T, THandler>(
        this IServiceCollection services,
        ServiceLifetime pipelineLifetime = ServiceLifetime.Scoped,
        ServiceLifetime? handlerLifetime = null)
        where THandler : class, IPipelineHandler<T>
    {
        ArgumentNullException.ThrowIfNull(services);

        ServiceLifetime resolvedHandlerLifetime = handlerLifetime ?? pipelineLifetime;

        if (pipelineLifetime == ServiceLifetime.Singleton && resolvedHandlerLifetime == ServiceLifetime.Scoped)
        {
            throw new InvalidOperationException(
                $"Cannot register Pipeline<{typeof(T).Name}> as Singleton with a Scoped "
                + "handler — a singleton pipeline cannot capture scoped dependencies.");
        }

        services.Replace(ServiceDescriptor.Describe(
            typeof(Pipeline<T>),
            typeof(Pipeline<T>),
            pipelineLifetime));

        services.Replace(ServiceDescriptor.Describe(
            typeof(IPipelineHandler<T>),
            typeof(THandler),
            resolvedHandlerLifetime));

        return services;
    }

    /// <summary>
    /// Registers a <see cref="Pipeline{T}"/> with a pre-built handler instance.
    /// The handler is registered as a singleton instance and is always
    /// compatible with any pipeline lifetime.
    /// </summary>
    /// <remarks>
    /// Use <see cref="PipelineHandler.FromDelegate{T}(Func{T, CancellationToken, Task})"/>
    /// (or one of its overloads) to wrap a delegate into an
    /// <see cref="IPipelineHandler{T}"/> before calling this method.
    /// </remarks>
    /// <typeparam name="T">The context type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="handlerInstance">The handler instance.</param>
    /// <param name="pipelineLifetime">Lifetime for <see cref="Pipeline{T}"/>. Defaults to <see cref="ServiceLifetime.Scoped"/>.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="services"/> or <paramref name="handlerInstance"/> is <c>null</c>.
    /// </exception>
    public static IServiceCollection AddPipeline<T>(
        this IServiceCollection services,
        IPipelineHandler<T> handlerInstance,
        ServiceLifetime pipelineLifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(handlerInstance);

        services.Replace(ServiceDescriptor.Describe(
            typeof(Pipeline<T>),
            typeof(Pipeline<T>),
            pipelineLifetime));

        services.Replace(ServiceDescriptor.Singleton(handlerInstance));

        return services;
    }

    /// <summary>
    /// Registers a middleware for <see cref="Pipeline{T}"/> without touching
    /// the pipeline registration itself. Use this to contribute middlewares to
    /// a pipeline that is owned and registered elsewhere.
    /// </summary>
    /// <remarks>
    /// The middleware is added via
    /// <see cref="ServiceCollectionDescriptorExtensions.TryAddEnumerable(IServiceCollection, ServiceDescriptor)"/>,
    /// so calling this twice with the same middleware type is a no-op.
    /// Middlewares execute in registration order. If <see cref="Pipeline{T}"/>
    /// is not registered, resolving it will fail — that is the caller's
    /// responsibility.
    /// </remarks>
    /// <typeparam name="T">The context type.</typeparam>
    /// <typeparam name="TMiddleware">The middleware class.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="lifetime">Lifetime for the middleware. Defaults to <see cref="ServiceLifetime.Scoped"/>.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> is <c>null</c>.</exception>
    public static IServiceCollection AddPipelineMiddleware<T, TMiddleware>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TMiddleware : class, IPipelineMiddleware<T>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Describe(
            typeof(IPipelineMiddleware<T>),
            typeof(TMiddleware),
            lifetime));

        return services;
    }

    /// <summary>
    /// Replaces the <see cref="IPipelineHandler{T}"/> registration with
    /// <typeparamref name="THandler"/>. Any existing handler registrations for
    /// <typeparamref name="T"/> are removed first.
    /// </summary>
    /// <typeparam name="T">The context type.</typeparam>
    /// <typeparam name="THandler">The replacement handler class.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="lifetime">Lifetime for the replacement handler. Defaults to <see cref="ServiceLifetime.Scoped"/>.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> is <c>null</c>.</exception>
    public static IServiceCollection ReplacePipelineHandler<T, THandler>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where THandler : class, IPipelineHandler<T>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Replace(ServiceDescriptor.Describe(
            typeof(IPipelineHandler<T>),
            typeof(THandler),
            lifetime));

        return services;
    }

    /// <summary>
    /// Registers <see cref="ActivityMiddleware{T}"/> as an
    /// <see cref="IPipelineMiddleware{T}"/> for <see cref="Pipeline{T}"/>.
    /// Equivalent to
    /// <c>AddPipelineMiddleware&lt;T, ActivityMiddleware&lt;T&gt;&gt;(lifetime)</c>
    /// but with the lifetime defaulting to <see cref="ServiceLifetime.Singleton"/>
    /// (the middleware is stateless).
    /// </summary>
    /// <remarks>
    /// Like <see cref="AddPipelineMiddleware{T, TMiddleware}"/>, this uses
    /// <see cref="ServiceCollectionDescriptorExtensions.TryAddEnumerable(IServiceCollection, ServiceDescriptor)"/>,
    /// so calling it twice for the same <typeparamref name="T"/> is a no-op.
    /// Call this before other <see cref="AddPipelineMiddleware{T, TMiddleware}"/>
    /// calls if you want the activity to wrap the full pipeline (outermost
    /// position); call it last to measure only the handler.
    /// </remarks>
    /// <typeparam name="T">The pipeline context type. Must implement <see cref="IPipelineContext"/>.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="lifetime">Lifetime for the middleware. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> is <c>null</c>.</exception>
    public static IServiceCollection AddPipelineTelemetry<T>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where T : class, IPipelineContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Describe(
            typeof(IPipelineMiddleware<T>),
            typeof(ActivityMiddleware<T>),
            lifetime));

        return services;
    }
}
