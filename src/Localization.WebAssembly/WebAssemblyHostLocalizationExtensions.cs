using System.Globalization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace ArchPillar.Extensions.Localization;

/// <summary>
/// Wires HTTP catalog loading for a Blazor WebAssembly client. A browser has no readable file system, so the
/// directory source finds nothing; catalogs are fetched over HTTP from the app's static web assets instead. This
/// registers the build-emitted manifest as a catalog provider on the ambient store and loads the active language
/// now, so the first render is localized and any other language is fetched the moment it is needed.
/// </summary>
public static class WebAssemblyHostLocalizationExtensions
{
    /// <summary>
    /// Registers the manifest at <paramref name="manifestUri"/> as a catalog provider on the ambient localizer and
    /// loads the active language (<see cref="CultureInfo.CurrentUICulture"/>) now. Call it on the built host before
    /// <c>RunAsync</c>. It uses the app's DI-registered <see cref="HttpClient"/> — the one the Blazor WebAssembly
    /// template registers over the host base address — so the container owns the client and the provider reuses it
    /// to fetch languages selected later.
    /// </summary>
    /// <param name="host">The Blazor WebAssembly host.</param>
    /// <param name="manifestUri">The manifest URI, relative to the client's base address, or absolute.</param>
    /// <param name="cancellationToken">A token to cancel the initial load.</param>
    /// <returns>A task that completes once the manifest provider is registered and the active language is loaded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No <see cref="HttpClient"/> is registered in the host's services.</exception>
    public static async Task UseArchPillarLocalizationAsync(
        this WebAssemblyHost host,
        string manifestUri = ManifestCatalogProvider.DefaultManifestPath,
        CancellationToken cancellationToken = default)
    {
        if (host is null)
        {
            throw new ArgumentNullException(nameof(host));
        }

        HttpClient httpClient = host.Services.GetRequiredService<HttpClient>();
        ManifestCatalogProvider provider = await ManifestCatalogProvider
            .CreateAsync(httpClient, manifestUri, Localizer.SourceCultureName, cancellationToken)
            .ConfigureAwait(false);
        Localizer.AddProvider(provider);
        await Localizer.LoadCultureAsync(CultureInfo.CurrentUICulture, cancellationToken).ConfigureAwait(false);
    }
}
