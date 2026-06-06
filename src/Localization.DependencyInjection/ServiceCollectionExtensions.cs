using ArchPillar.Extensions.Localization;
using ArchPillar.Extensions.Localization.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers ArchPillar localization with the dependency-injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="Localizer"/> and adapts it to <see cref="IStringLocalizer"/>,
    /// <see cref="IStringLocalizer{T}"/>, and <see cref="IStringLocalizerFactory"/>.
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

        services.AddSingleton(_ => new Localizer(options ?? new LocalizerOptions()));
        return AddAdapters(services);
    }

    /// <summary>
    /// Registers an already-constructed singleton <see cref="Localizer"/> and the same
    /// <see cref="IStringLocalizer"/> adapters. Use this when the localizer cannot be built from a
    /// directory — for example in Blazor WebAssembly, where catalogs are fetched over HTTP and the
    /// localizer is created with <see cref="Localizer.FromCatalogs"/> before registration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="localizer">The localizer instance to register.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="localizer"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddArchPillarLocalization(this IServiceCollection services, Localizer localizer)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (localizer is null)
        {
            throw new ArgumentNullException(nameof(localizer));
        }

        services.AddSingleton(localizer);
        return AddAdapters(services);
    }

    private static IServiceCollection AddAdapters(IServiceCollection services)
    {
        services.AddSingleton<IStringLocalizerFactory, LocalizerStringLocalizerFactory>();
        services.AddSingleton<IStringLocalizer, LocalizerStringLocalizer>();
        services.AddSingleton(typeof(IStringLocalizer<>), typeof(LocalizerStringLocalizer<>));
        return services;
    }
}
