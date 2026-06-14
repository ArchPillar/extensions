using ArchPillar.Extensions.Localization;
using ArchPillar.Extensions.Localization.Formats;
using Microsoft.AspNetCore.StaticFiles;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Serves ArchPillar translation catalogs as static files. The static file middleware returns 404 for an
/// unknown file extension by default, so a hosted Blazor WebAssembly client fetching its <c>.arb</c> (or
/// <c>.xliff</c> / <c>.po</c>) catalogs over HTTP gets nothing and silently falls back to its in-code defaults.
/// These helpers register the catalog content types so the catalogs are served.
/// </summary>
public static class TranslationStaticFileExtensions
{
    // The catalog formats this library bundles, each with the content type its files are served as. Extensions
    // are taken from the format providers, so a format that adds an extension is covered without changes here.
    private static readonly (ITranslationFormat Format, string ContentType)[] _formats =
    [
        (new ArbTranslationFormat(), "application/json"),
        (new XliffTranslationFormat(), "application/xml"),
        (new PoTranslationFormat(), "text/plain"),
    ];

    /// <summary>
    /// Registers the translation catalog content types (<c>.arb</c>, <c>.xliff</c>, <c>.xlf</c>, <c>.po</c>,
    /// <c>.pot</c>) on <paramref name="provider"/>, so a static file middleware using it serves the catalogs
    /// instead of 404-ing them. Pass the returned provider to each <c>UseStaticFiles</c> that should serve them.
    /// </summary>
    /// <param name="provider">The content-type provider to register the catalog types on.</param>
    /// <returns>The same provider, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="provider"/> is <see langword="null"/>.</exception>
    public static FileExtensionContentTypeProvider AddArchPillarTranslationFormats(this FileExtensionContentTypeProvider provider)
    {
        if (provider is null)
        {
            throw new ArgumentNullException(nameof(provider));
        }

        foreach ((ITranslationFormat format, var contentType) in _formats)
        {
            foreach (var extension in format.Extensions)
            {
                provider.Mappings[extension] = contentType;
            }
        }

        return provider;
    }

    /// <summary>
    /// Adds a static file middleware that serves the translation catalog formats (in addition to the standard
    /// types), optionally scoped to <paramref name="requestPath"/>. Convenience for the common case; an app that
    /// already configures <c>UseStaticFiles</c> can instead pass <see cref="AddArchPillarTranslationFormats"/>'s
    /// provider to its existing options.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="requestPath">The request path prefix to serve under (for example <c>/party</c>), or <see langword="null"/> for the root.</param>
    /// <returns>The same application builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is <see langword="null"/>.</exception>
    public static IApplicationBuilder UseArchPillarTranslationFiles(this IApplicationBuilder app, string? requestPath = null)
    {
        if (app is null)
        {
            throw new ArgumentNullException(nameof(app));
        }

        var provider = new FileExtensionContentTypeProvider();
        provider.AddArchPillarTranslationFormats();

        var options = new StaticFileOptions { ContentTypeProvider = provider };
        if (!string.IsNullOrEmpty(requestPath))
        {
            options.RequestPath = requestPath;
        }

        return app.UseStaticFiles(options);
    }
}
