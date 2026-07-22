using System.Globalization;
using ArchPillar.Extensions.Localization.Providers;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace ArchPillar.Extensions.Localization;

/// <summary>
/// Wires HTTP catalog loading for a Blazor WebAssembly client. A browser has no readable file system, so the
/// directory source finds nothing; catalogs are fetched over HTTP from the app's static web assets instead. This
/// fetches the build-emitted manifest, configures the ambient store with it as a catalog provider, and loads the
/// active language now, so the first render is localized and any other language is fetched the moment it is needed.
/// </summary>
public static class WebAssemblyHostLocalizationExtensions
{
    /// <summary>
    /// Builds an HTTP <see cref="ManifestCatalogProvider"/> from the manifest at <paramref name="manifestUri"/> and
    /// configures the ambient localizer with it (layered above <paramref name="options"/>'s providers), then loads
    /// the active language (<see cref="CultureInfo.CurrentUICulture"/>) now. Call it on the built host before
    /// <c>RunAsync</c>, passing the same <paramref name="options"/> used to register localization in DI — the
    /// provider can only be built from the host's <see cref="HttpClient"/> after the host is built, so this is where
    /// the final configuration happens. It uses the app's DI-registered <see cref="HttpClient"/> (the one the Blazor
    /// WebAssembly template registers over the host base address), so the provider reuses it for later languages.
    /// </summary>
    /// <param name="host">The Blazor WebAssembly host.</param>
    /// <param name="options">
    /// The localizer options to configure with. Required, and MUST be the same <see cref="LocalizerOptions"/>
    /// instance (or an equal one) passed to <c>AddArchPillarLocalization</c> in DI: this call reconfigures the
    /// ambient store from scratch with these options, so any <see cref="LocalizerOptions.SourceCulture"/>,
    /// <see cref="LocalizerOptions.Formats"/>, hot-reload, culture allow-list, or provider setting the DI
    /// registration made is silently dropped if a different (or default) instance is passed here.
    /// </param>
    /// <param name="manifestUri">The manifest URI, relative to the client's base address, or absolute.</param>
    /// <param name="cancellationToken">A token to cancel the initial load.</param>
    /// <returns>A task that completes once the store is configured and the active language is loaded.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="host"/> or <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">No <see cref="HttpClient"/> is registered in the host's services.</exception>
    public static async Task UseArchPillarLocalizationAsync(
        this WebAssemblyHost host,
        LocalizerOptions options,
        string manifestUri = ManifestCatalogProvider.DefaultManifestPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(options);

        HttpClient httpClient = host.Services.GetRequiredService<HttpClient>();
        ManifestCatalogProvider provider = await ManifestCatalogProvider
            .CreateAsync(httpClient, manifestUri, options.SourceCulture, options.Formats, cancellationToken)
            .ConfigureAwait(false);

        Localizer.Configure(options with { Providers = [.. options.Providers, _ => provider] });
        await Localizer.LoadCultureAsync(CultureInfo.CurrentUICulture, cancellationToken).ConfigureAwait(false);
    }
}
