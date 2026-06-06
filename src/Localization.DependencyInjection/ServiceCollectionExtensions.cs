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
        services.AddSingleton<IStringLocalizerFactory, LocalizerStringLocalizerFactory>();
        services.AddSingleton<IStringLocalizer, LocalizerStringLocalizer>();
        services.AddSingleton(typeof(IStringLocalizer<>), typeof(LocalizerStringLocalizer<>));
        return services;
    }
}
