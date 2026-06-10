using ArchPillar.Extensions.Localization;
using ArchPillar.Extensions.Localization.DependencyInjection;
using Ambient = ArchPillar.Extensions.Localization.Localization;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers ArchPillar localization with the dependency-injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Configures the ambient <see cref="Localization"/> store from <paramref name="options"/> and registers
    /// the native localization views — <see cref="ILocalizer"/>, <see cref="ILocalizer{T}"/>, and a concrete
    /// <see cref="Localizer"/> for direct injection — over it, so an injected localizer, a non-DI caller, and
    /// an exception text all read the same store (Decision D-I). For <c>IStringLocalizer</c> interop while
    /// migrating an existing codebase, add the <c>ArchPillar.Extensions.Localization.StringLocalizer</c>
    /// package and call <c>AddArchPillarStringLocalizer</c> instead (Decision D-J).
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
        // process-global ambient mutation below dangerous) is a no-op rather than stacking duplicate sources.
        // The first registration's options win.
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
        return services;
    }
}
