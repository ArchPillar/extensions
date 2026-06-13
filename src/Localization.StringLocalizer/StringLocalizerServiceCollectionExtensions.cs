using ArchPillar.Extensions.Localization;
using ArchPillar.Extensions.Localization.StringLocalizer;
using Microsoft.Extensions.Localization;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the <see cref="IStringLocalizer"/> interop adapters for ArchPillar localization — the migration
/// on-ramp for a codebase adopting the library from an existing <see cref="IStringLocalizer"/>/<c>.resx</c>
/// setup. Once your code no longer depends on <see cref="IStringLocalizer"/>, drop this package and keep the
/// native <c>AddArchPillarLocalization</c> registration.
/// </summary>
public static class StringLocalizerServiceCollectionExtensions
{
    /// <summary>
    /// Registers the native ArchPillar localization views (via <c>AddArchPillarLocalization</c>) and adapts
    /// them to <see cref="IStringLocalizer"/>, <see cref="IStringLocalizer{T}"/>, and
    /// <see cref="IStringLocalizerFactory"/>, composing over the ResourceManager/<c>.resx</c> factory so
    /// existing translations keep resolving on an ambient miss (Decision D-J). Call this instead of
    /// <c>AddArchPillarLocalization</c> while migrating an <see cref="IStringLocalizer"/> codebase.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">The localizer options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddArchPillarStringLocalizer(this IServiceCollection services, LocalizerOptions? options = null)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        // The native registration (ambient configuration + ILocalizer views) underpins the adapters; it is
        // idempotent, so it is safe whether or not the host already called AddArchPillarLocalization.
        services.AddArchPillarLocalization(options);

        // Idempotent per collection: a second call must not stack a second composing factory over the first.
        // The native call above keys off the DefaultLocalizer descriptor, which is already present after the first
        // AddArchPillarLocalization, so the interop block needs its own marker.
        if (services.Any(descriptor => descriptor.ServiceType == typeof(StringLocalizerMarker)))
        {
            return services;
        }

        services.AddSingleton<StringLocalizerMarker>();

        // IStringLocalizer adapters over the ambient store, composing over the ResourceManager/.resx factory
        // so existing translations keep resolving on an ambient miss (Decision D-J). AddLocalization is called
        // here (it TryAdds, so it is a no-op when the host already added it, or when a custom factory is
        // present) so the .resx factory is registered regardless of whether the host calls AddLocalization
        // before or after us — otherwise a host calling it after us would have its TryAdd suppressed by our
        // own factory registration and silently lose all .resx resolution (Decision D-F3). The generic
        // IStringLocalizer<T> flows through the BCL StringLocalizer<T>, which calls our factory's
        // Create(typeof(T)).
        services.AddLocalization();
        ServiceDescriptor? innerFactory = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(IStringLocalizerFactory));
        services.AddSingleton<IStringLocalizerFactory>(provider =>
            new LocalizerStringLocalizerFactory(ResolveInnerFactory(provider, innerFactory)));
        services.AddSingleton(typeof(IStringLocalizer<>), typeof(StringLocalizer<>));
        services.AddSingleton<IStringLocalizer>(_ => new LocalizerStringLocalizer(string.Empty, inner: null));
        return services;
    }

    // Builds the previously-registered factory from its descriptor so the composing factory can wrap it.
    // Returns null when there was no prior factory (then the adapter just falls back to the in-code default)
    // or when the descriptor is keyed (whose implementation members are not readable this way).
    private static IStringLocalizerFactory? ResolveInnerFactory(IServiceProvider provider, ServiceDescriptor? descriptor)
    {
        if (descriptor is null)
        {
            return null;
        }

        // A keyed descriptor's implementation members throw when read off the descriptor, so leave it alone.
        if (descriptor.IsKeyedService)
        {
            return null;
        }

        if (descriptor.ImplementationInstance is IStringLocalizerFactory instance)
        {
            return instance;
        }

        if (descriptor.ImplementationFactory is { } factory)
        {
            return factory(provider) as IStringLocalizerFactory;
        }

        if (descriptor.ImplementationType is null)
        {
            return null;
        }

        try
        {
            return (IStringLocalizerFactory)ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType);
        }
        catch (InvalidOperationException)
        {
            // The inner factory (e.g. the ResourceManager one we registered via AddLocalization) could not be
            // constructed because a dependency it needs — typically ILoggerFactory — is not registered. Degrade
            // to no inner factory rather than failing every IStringLocalizer resolution; the adapter then uses
            // the ambient store and the in-code default, which is exactly the behaviour with no .resx present.
            return null;
        }
    }
}
