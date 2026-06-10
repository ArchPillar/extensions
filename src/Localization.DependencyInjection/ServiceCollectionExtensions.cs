using ArchPillar.Extensions.Localization;
using ArchPillar.Extensions.Localization.DependencyInjection;
using Microsoft.Extensions.Localization;
using Ambient = ArchPillar.Extensions.Localization.Localization;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers ArchPillar localization with the dependency-injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Configures the ambient <see cref="Localization"/> store from <paramref name="options"/> and registers
    /// <see cref="ILocalizer"/>, <see cref="ILocalizer{T}"/>, <see cref="IStringLocalizer"/>,
    /// <see cref="IStringLocalizer{T}"/>, and <see cref="IStringLocalizerFactory"/> over it — so an injected
    /// localizer, a non-DI caller, and an exception text all read the same store (Decision D-I). A concrete
    /// <see cref="Localizer"/> over the same options is also registered for direct injection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">The localizer options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddArchPillarLocalization(this IServiceCollection services, LocalizerOptions? options = null)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        // Idempotent per collection: a second call (a common double-registration footgun, and what makes the
        // process-global ambient mutation below dangerous) is a no-op rather than stacking duplicate sources
        // and chaining the composing factory over itself. The first registration's options win.
        if (services.Any(descriptor => descriptor.ServiceType == typeof(Localizer)))
        {
            return services;
        }

        LocalizerOptions resolved = options ?? new LocalizerOptions();

        // Feed the ambient store so DI and the ambient share one source of truth — including the formatting
        // policy, so an injected interface, a non-DI caller, and the directly injected Localizer agree.
        Ambient.SourceCulture = resolved.SourceCulture;
        Ambient.MissingArguments = resolved.MissingArguments;
        if (!string.IsNullOrEmpty(resolved.TranslationsDirectory))
        {
            Ambient.TranslationsDirectory = resolved.TranslationsDirectory;
        }

        foreach (ITranslationSource source in resolved.Sources)
        {
            Ambient.AddSource(source);
        }

        // Native interfaces over the ambient store.
        services.AddSingleton(Ambient.Default);
        services.AddSingleton(typeof(ILocalizer<>), typeof(AmbientLocalizer<>));

        // A concrete Localizer over the same options remains available for direct injection.
        services.AddSingleton(_ => new Localizer(resolved));

        // IStringLocalizer adapters over the ambient store, composing over the ResourceManager/.resx factory
        // so existing translations keep resolving on an ambient miss (Decision D-J). AddLocalization is called
        // here (it TryAdds, so it is a no-op when the host already added it, or when a custom factory is
        // present) so the .resx factory is registered regardless of whether the host calls AddLocalization
        // before or after us — otherwise a host calling it after us would have its TryAdd suppressed by our
        // own factory registration and silently lose all .resx resolution. The generic IStringLocalizer<T>
        // flows through the BCL StringLocalizer<T>, which calls our factory's Create(typeof(T)).
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
