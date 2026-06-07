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

        LocalizerOptions resolved = options ?? new LocalizerOptions();

        // Feed the ambient store so DI and the ambient share one source of truth.
        Ambient.SourceCulture = resolved.SourceCulture;
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

        // IStringLocalizer adapters over the ambient store.
        services.AddSingleton<IStringLocalizerFactory, LocalizerStringLocalizerFactory>();
        services.AddSingleton<IStringLocalizer, LocalizerStringLocalizer>();
        services.AddSingleton(typeof(IStringLocalizer<>), typeof(LocalizerStringLocalizer<>));
        return services;
    }
}
