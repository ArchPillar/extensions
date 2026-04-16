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
/// <see cref="ServiceLifetime"/>. The only combination that is rejected at
/// registration time is a <see cref="ServiceLifetime.Singleton"/>
/// <see cref="Pipeline{T}"/> with a <see cref="ServiceLifetime.Scoped"/>
/// middleware or handler — the classic captive-dependency bug.
/// </para>
/// <para>
/// <see cref="AddPipeline{T, THandler}(IServiceCollection, ServiceLifetime, ServiceLifetime?)"/>
/// registers <see cref="Pipeline{T}"/> alongside its required handler.
/// <see cref="AddPipelineMiddleware{T, TMiddleware}(IServiceCollection, ServiceLifetime)"/>
/// registers a middleware without touching the pipeline registration, so
/// library modules can contribute middlewares to a pipeline owned elsewhere.
/// <see cref="ReplacePipelineHandler{T, THandler}(IServiceCollection, ServiceLifetime)"/>
/// swaps out an existing handler — useful for tests and overrides.
/// </para>
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <see cref="Pipeline{T}"/> with a required terminal handler.
    /// </summary>
    /// <remarks>
    /// The pipeline is registered as a self-constructed service — DI resolves
    /// it by invoking its public constructor with the registered
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
    /// Thrown if the pipeline is registered as <see cref="ServiceLifetime.Singleton"/>
    /// and the handler or any already-registered middleware is
    /// <see cref="ServiceLifetime.Scoped"/>.
    /// </exception>
    public static IServiceCollection AddPipeline<T, THandler>(
        this IServiceCollection services,
        ServiceLifetime pipelineLifetime = ServiceLifetime.Scoped,
        ServiceLifetime? handlerLifetime = null)
        where THandler : class, IPipelineHandler<T>
    {
        ArgumentNullException.ThrowIfNull(services);

        ServiceLifetime resolvedHandlerLifetime = handlerLifetime ?? pipelineLifetime;

        EnsurePipelineLifetimeCompatibleWithExistingSteps<T>(services, pipelineLifetime);
        EnsureStepLifetimeCompatibleWithPipeline<T>(services, resolvedHandlerLifetime, pipelineLifetime);

        services.TryAdd(ServiceDescriptor.Describe(
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
    /// Registers a <see cref="Pipeline{T}"/> with an async delegate handler.
    /// The handler is wrapped as a singleton instance.
    /// </summary>
    /// <typeparam name="T">The context type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="handler">The terminal handler delegate.</param>
    /// <param name="pipelineLifetime">Lifetime for <see cref="Pipeline{T}"/>. Defaults to <see cref="ServiceLifetime.Scoped"/>.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> or <paramref name="handler"/> is <c>null</c>.</exception>
    public static IServiceCollection AddPipeline<T>(
        this IServiceCollection services,
        Func<T, CancellationToken, Task> handler,
        ServiceLifetime pipelineLifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(handler);
        return AddPipelineWithInstance(services, PipelineHandler.FromDelegate(handler), pipelineLifetime);
    }

    /// <summary>
    /// Registers a <see cref="Pipeline{T}"/> with an async delegate handler
    /// that does not observe the cancellation token. The handler is wrapped as
    /// a singleton instance.
    /// </summary>
    /// <typeparam name="T">The context type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="handler">The terminal handler delegate.</param>
    /// <param name="pipelineLifetime">Lifetime for <see cref="Pipeline{T}"/>. Defaults to <see cref="ServiceLifetime.Scoped"/>.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> or <paramref name="handler"/> is <c>null</c>.</exception>
    public static IServiceCollection AddPipeline<T>(
        this IServiceCollection services,
        Func<T, Task> handler,
        ServiceLifetime pipelineLifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(handler);
        return AddPipelineWithInstance(services, PipelineHandler.FromDelegate(handler), pipelineLifetime);
    }

    /// <summary>
    /// Registers a <see cref="Pipeline{T}"/> with a synchronous delegate
    /// handler. The handler is wrapped as a singleton instance.
    /// </summary>
    /// <typeparam name="T">The context type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="handler">The terminal handler delegate.</param>
    /// <param name="pipelineLifetime">Lifetime for <see cref="Pipeline{T}"/>. Defaults to <see cref="ServiceLifetime.Scoped"/>.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> or <paramref name="handler"/> is <c>null</c>.</exception>
    public static IServiceCollection AddPipeline<T>(
        this IServiceCollection services,
        Action<T> handler,
        ServiceLifetime pipelineLifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(handler);
        return AddPipelineWithInstance(services, PipelineHandler.FromDelegate(handler), pipelineLifetime);
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
    /// <exception cref="InvalidOperationException">
    /// Thrown if <see cref="Pipeline{T}"/> is already registered as
    /// <see cref="ServiceLifetime.Singleton"/> and <paramref name="lifetime"/>
    /// is <see cref="ServiceLifetime.Scoped"/>.
    /// </exception>
    public static IServiceCollection AddPipelineMiddleware<T, TMiddleware>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TMiddleware : class, IPipelineMiddleware<T>
    {
        ArgumentNullException.ThrowIfNull(services);

        EnsureStepLifetimeCompatibleWithPipeline<T>(services, lifetime, pipelineLifetime: null);

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
    /// <exception cref="InvalidOperationException">
    /// Thrown if <see cref="Pipeline{T}"/> is already registered as
    /// <see cref="ServiceLifetime.Singleton"/> and <paramref name="lifetime"/>
    /// is <see cref="ServiceLifetime.Scoped"/>.
    /// </exception>
    public static IServiceCollection ReplacePipelineHandler<T, THandler>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where THandler : class, IPipelineHandler<T>
    {
        ArgumentNullException.ThrowIfNull(services);

        EnsureStepLifetimeCompatibleWithPipeline<T>(services, lifetime, pipelineLifetime: null);

        services.Replace(ServiceDescriptor.Describe(
            typeof(IPipelineHandler<T>),
            typeof(THandler),
            lifetime));

        return services;
    }

    /// <summary>
    /// Replaces the <see cref="IPipelineHandler{T}"/> registration with an
    /// async delegate handler. Any existing handler registrations for
    /// <typeparamref name="T"/> are removed first. The replacement is
    /// registered as a singleton instance.
    /// </summary>
    /// <typeparam name="T">The context type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="handler">The replacement handler delegate.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> or <paramref name="handler"/> is <c>null</c>.</exception>
    public static IServiceCollection ReplacePipelineHandler<T>(
        this IServiceCollection services,
        Func<T, CancellationToken, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(handler);

        services.Replace(ServiceDescriptor.Singleton(
            typeof(IPipelineHandler<T>),
            PipelineHandler.FromDelegate(handler)));

        return services;
    }

    /// <summary>
    /// Replaces the <see cref="IPipelineHandler{T}"/> registration with an
    /// async delegate handler. Any existing handler registrations for
    /// <typeparamref name="T"/> are removed first. The replacement is
    /// registered as a singleton instance.
    /// </summary>
    /// <typeparam name="T">The context type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="handler">The replacement handler delegate.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> or <paramref name="handler"/> is <c>null</c>.</exception>
    public static IServiceCollection ReplacePipelineHandler<T>(
        this IServiceCollection services,
        Func<T, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(handler);

        services.Replace(ServiceDescriptor.Singleton(
            typeof(IPipelineHandler<T>),
            PipelineHandler.FromDelegate(handler)));

        return services;
    }

    /// <summary>
    /// Replaces the <see cref="IPipelineHandler{T}"/> registration with a
    /// synchronous delegate handler. Any existing handler registrations for
    /// <typeparamref name="T"/> are removed first. The replacement is
    /// registered as a singleton instance.
    /// </summary>
    /// <typeparam name="T">The context type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="handler">The replacement handler delegate.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="services"/> or <paramref name="handler"/> is <c>null</c>.</exception>
    public static IServiceCollection ReplacePipelineHandler<T>(
        this IServiceCollection services,
        Action<T> handler)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(handler);

        services.Replace(ServiceDescriptor.Singleton(
            PipelineHandler.FromDelegate(handler)));

        return services;
    }

    private static IServiceCollection AddPipelineWithInstance<T>(
        IServiceCollection services,
        IPipelineHandler<T> handlerInstance,
        ServiceLifetime pipelineLifetime)
    {
        // Singleton instance is always compatible — no lifetime check needed
        // for the handler itself; only verify the pipeline lifetime is
        // compatible with any already-registered scoped middlewares/handlers.
        EnsurePipelineLifetimeCompatibleWithExistingSteps<T>(services, pipelineLifetime);

        services.TryAdd(ServiceDescriptor.Describe(
            typeof(Pipeline<T>),
            typeof(Pipeline<T>),
            pipelineLifetime));

        services.Replace(ServiceDescriptor.Singleton(handlerInstance));

        return services;
    }

    private static void EnsurePipelineLifetimeCompatibleWithExistingSteps<T>(
        IServiceCollection services,
        ServiceLifetime pipelineLifetime)
    {
        if (pipelineLifetime != ServiceLifetime.Singleton)
        {
            return;
        }

        foreach (ServiceDescriptor descriptor in services)
        {
            Type serviceType = descriptor.ServiceType;
            if ((serviceType == typeof(IPipelineMiddleware<T>) || serviceType == typeof(IPipelineHandler<T>))
                && descriptor.Lifetime == ServiceLifetime.Scoped)
            {
                throw new InvalidOperationException(
                    $"Cannot register Pipeline<{typeof(T).Name}> as Singleton while a scoped "
                    + $"{serviceType.Name} is already registered. A singleton pipeline cannot "
                    + $"capture scoped dependencies.");
            }
        }
    }

    private static void EnsureStepLifetimeCompatibleWithPipeline<T>(
        IServiceCollection services,
        ServiceLifetime stepLifetime,
        ServiceLifetime? pipelineLifetime)
    {
        if (stepLifetime != ServiceLifetime.Scoped)
        {
            return;
        }

        ServiceLifetime? effectivePipelineLifetime = pipelineLifetime;
        if (effectivePipelineLifetime is null)
        {
            foreach (ServiceDescriptor descriptor in services)
            {
                if (descriptor.ServiceType == typeof(Pipeline<T>))
                {
                    effectivePipelineLifetime = descriptor.Lifetime;
                    break;
                }
            }
        }

        if (effectivePipelineLifetime == ServiceLifetime.Singleton)
        {
            throw new InvalidOperationException(
                $"Cannot register a scoped middleware or handler for a singleton "
                + $"Pipeline<{typeof(T).Name}>. A singleton pipeline cannot capture "
                + $"scoped dependencies.");
        }
    }
}
