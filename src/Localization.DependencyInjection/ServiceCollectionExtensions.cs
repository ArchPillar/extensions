using ArchPillar.Extensions.Localization;
using ArchPillar.Extensions.Localization.DependencyInjection;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers ArchPillar localization with the dependency-injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Configures the ambient <see cref="Localizer"/> store from <paramref name="options"/> and registers
    /// the native localization views — <see cref="ILocalizer"/>, <see cref="ILocalizer{T}"/>, and
    /// <see cref="ILocalizerFactory"/> — over it, so an injected localizer, a non-DI caller, and an exception text
    /// all read the same store (Decision D-I). For <c>IStringLocalizer</c> interop while
    /// migrating an existing codebase, add the <c>ArchPillar.Extensions.Localization.StringLocalizer</c>
    /// package and call <c>AddArchPillarStringLocalizer</c> instead (Decision D-J).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">The localizer options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddArchPillarLocalization(this IServiceCollection services, LocalizerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Idempotent per collection: a second call (a common double-registration footgun) is a no-op rather
        // than stacking duplicate registrations. The first registration's options win.
        if (services.Any(descriptor => descriptor.ServiceType == typeof(LocalizationContext)))
        {
            return services;
        }

        LocalizerOptions resolved = options ?? new LocalizerOptions();

        // Feed and register the single process-wide ambient context, so DI, a non-DI caller, and an exception
        // text all read one store. Registered as an instance, so the container does not dispose the
        // process-global ambient.
        Localizer.Initialize(resolved);
        services.AddSingleton(Localizer.Ambient);

        // The native views over the ambient context. The context is itself the ILocalizerFactory (the
        // ILoggerFactory-shaped door), so it is registered as that view rather than a separate factory type.
        services.AddSingleton<ILocalizer>(provider => provider.GetRequiredService<LocalizationContext>().Default);
        services.AddSingleton(typeof(ILocalizer<>), typeof(InjectedLocalizer<>));
        services.AddSingleton<ILocalizerFactory>(provider => provider.GetRequiredService<LocalizationContext>());
        return services;
    }
}
