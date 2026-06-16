using System.Globalization;
using System.Text.Json;
using ArchPillar.Extensions.Localization.Formats;

namespace ArchPillar.Extensions.Localization;

/// <summary>
/// Loads translation catalogs over HTTP into a <see cref="LocalizationContext"/> — the client-side
/// counterpart to the file-system directory source, for a host with no readable file system such as Blazor
/// WebAssembly. Catalogs are fetched as static web assets, parsed by the bundled format providers (resolved
/// by file extension), and layered into the store. The in-code default stays the terminal fallback, so a
/// missing, unrecognised, or malformed catalog is skipped rather than fatal.
/// </summary>
public static class HttpCatalogLoaderExtensions
{
    /// <summary>The manifest file name the build emits and the loader reads.</summary>
    public const string DefaultManifestFileName = "apl-catalogs.json";

    /// <summary>
    /// The default manifest location, relative to the client's <see cref="HttpClient.BaseAddress"/>. The build
    /// writes this index beside the catalogs (and regenerates it after the publish-time merge), so the one path
    /// resolves the right files in both the development and the published layout.
    /// </summary>
    public const string DefaultManifestPath = "Translations/" + DefaultManifestFileName;

    /// <summary>
    /// Fetches each catalog in <paramref name="requestUris"/> over <paramref name="httpClient"/>, parses it with
    /// the format provider matching its file extension, and layers it into <paramref name="context"/>. A URI that
    /// is missing (a non-success response or a network failure), has no matching format, or fails to parse is
    /// skipped, so a partial deployment degrades to the in-code defaults rather than throwing.
    /// </summary>
    /// <param name="context">The localization context to load into.</param>
    /// <param name="httpClient">The client used to fetch the catalogs; its base address resolves a relative URI.</param>
    /// <param name="requestUris">The catalog URIs to fetch, each relative to the client's base address, or absolute.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of catalogs successfully loaded.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static async Task<int> AddCatalogsFromHttpAsync(
        this LocalizationContext context,
        HttpClient httpClient,
        IEnumerable<string> requestUris,
        CancellationToken cancellationToken = default)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (httpClient is null)
        {
            throw new ArgumentNullException(nameof(httpClient));
        }

        if (requestUris is null)
        {
            throw new ArgumentNullException(nameof(requestUris));
        }

        TranslationFormatRegistry registry = BuildRegistry();
        var added = 0;
        foreach (var requestUri in requestUris)
        {
            Catalog? catalog = await TryReadCatalogAsync(httpClient, requestUri, registry, cancellationToken).ConfigureAwait(false);
            if (catalog is not null)
            {
                context.AddCatalog(catalog);
                added++;
            }
        }

        return added;
    }

    /// <summary>
    /// Fetches the catalog manifest at <paramref name="manifestUri"/>, then fetches and layers in every catalog
    /// it lists (each resolved relative to the manifest). The build emits and maintains the manifest, so this is
    /// the discovery-based counterpart to <see cref="AddCatalogsFromHttpAsync"/>: it needs no hand-maintained
    /// file list and stays correct across the development and the published catalog layout. A missing manifest
    /// loads nothing, leaving the app on its in-code defaults.
    /// </summary>
    /// <param name="context">The localization context to load into.</param>
    /// <param name="httpClient">The client used to fetch the manifest and catalogs; its base address resolves a relative URI.</param>
    /// <param name="manifestUri">The manifest URI, relative to the client's base address, or absolute.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of catalogs successfully loaded.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static async Task<int> AddCatalogsFromManifestAsync(
        this LocalizationContext context,
        HttpClient httpClient,
        string manifestUri = DefaultManifestPath,
        CancellationToken cancellationToken = default)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (httpClient is null)
        {
            throw new ArgumentNullException(nameof(httpClient));
        }

        if (manifestUri is null)
        {
            throw new ArgumentNullException(nameof(manifestUri));
        }

        IReadOnlyList<string> catalogUris = await TryReadManifestAsync(httpClient, manifestUri, cancellationToken).ConfigureAwait(false);
        if (catalogUris.Count == 0)
        {
            return 0;
        }

        return await context.AddCatalogsFromHttpAsync(httpClient, catalogUris, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// As <see cref="AddCatalogsFromManifestAsync(LocalizationContext, HttpClient, string, CancellationToken)"/>,
    /// but fetches only the catalogs <paramref name="culture"/> needs — that culture, its parent chain, and the
    /// source language — rather than every catalog in the manifest. The on-demand counterpart for a Blazor
    /// WebAssembly client that should download just the active language; call it again on a culture switch to pull
    /// the newly selected one in (an already-loaded culture's files are simply re-layered, harmlessly).
    /// </summary>
    /// <param name="context">The localization context to load into.</param>
    /// <param name="httpClient">The client used to fetch the manifest and catalogs; its base address resolves a relative URI.</param>
    /// <param name="culture">The culture whose catalogs to fetch, with its parent chain and the source language.</param>
    /// <param name="manifestUri">The manifest URI, relative to the client's base address, or absolute.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of catalogs successfully loaded.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static async Task<int> AddCatalogsFromManifestAsync(
        this LocalizationContext context,
        HttpClient httpClient,
        CultureInfo culture,
        string manifestUri = DefaultManifestPath,
        CancellationToken cancellationToken = default)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (httpClient is null)
        {
            throw new ArgumentNullException(nameof(httpClient));
        }

        if (culture is null)
        {
            throw new ArgumentNullException(nameof(culture));
        }

        if (manifestUri is null)
        {
            throw new ArgumentNullException(nameof(manifestUri));
        }

        HashSet<string> wanted = CultureChain(culture);
        wanted.Add(context.SourceCultureName);

        IReadOnlyList<string> catalogUris = await TryReadManifestAsync(httpClient, manifestUri, cancellationToken).ConfigureAwait(false);
        var scoped = catalogUris.Where(uri => wanted.Contains(CultureFromUri(uri))).ToList();
        if (scoped.Count == 0)
        {
            return 0;
        }

        return await context.AddCatalogsFromHttpAsync(httpClient, scoped, cancellationToken).ConfigureAwait(false);
    }

    // The culture and its parent chain (de-AT -> de), by name, for a culture-scoped fetch.
    private static HashSet<string> CultureChain(CultureInfo culture)
    {
        var chain = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (CultureInfo? current = culture; current is not null && !string.IsNullOrEmpty(current.Name); current = current.Parent)
        {
            chain.Add(current.Name);
        }

        return chain;
    }

    // The culture tag a catalog URI's file name ends with: Translations/App.de.xliff -> "de", de.arb -> "de".
    private static string CultureFromUri(string requestUri)
    {
        var end = requestUri.IndexOfAny(['?', '#']);
        var path = end >= 0 ? requestUri[..end] : requestUri;
        var name = Path.GetFileNameWithoutExtension(path);
        var lastDot = name.LastIndexOf('.');
        return lastDot >= 0 ? name[(lastDot + 1)..] : name;
    }

    private static async Task<Catalog?> TryReadCatalogAsync(
        HttpClient httpClient,
        string requestUri,
        TranslationFormatRegistry registry,
        CancellationToken cancellationToken)
    {
        ITranslationFormat? format = registry.ResolveByExtension(ExtensionOf(requestUri));
        if (format is null)
        {
            return null;
        }

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return null;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            try
            {
                using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                return await format.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A malformed catalog must not take down the app; skip it and keep the in-code default.
                return null;
            }
        }
    }

    private static async Task<IReadOnlyList<string>> TryReadManifestAsync(
        HttpClient httpClient,
        string manifestUri,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(manifestUri, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return [];
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            try
            {
                using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using JsonDocument document = await JsonDocument.ParseAsync(stream, default, cancellationToken).ConfigureAwait(false);
                return ParseManifest(document.RootElement, manifestUri);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A malformed manifest loads nothing rather than throwing during startup.
                return [];
            }
        }
    }

    private static IReadOnlyList<string> ParseManifest(JsonElement root, string manifestUri)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("catalogs", out JsonElement catalogs)
            || catalogs.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var baseUri = ManifestBase(manifestUri);
        var uris = new List<string>();
        foreach (JsonElement entry in catalogs.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("file", out JsonElement file)
                || file.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var name = file.GetString();
            if (!string.IsNullOrEmpty(name))
            {
                uris.Add(Resolve(baseUri, name));
            }
        }

        return uris;
    }

    // The manifest lists bare file names; each resolves against the manifest's own directory so the catalogs are
    // fetched from the same folder, regardless of where that folder is mounted.
    private static string ManifestBase(string manifestUri)
    {
        var slash = manifestUri.LastIndexOf('/');
        return slash >= 0 ? manifestUri[..(slash + 1)] : string.Empty;
    }

    private static string Resolve(string baseUri, string file) =>
        file.StartsWith('/') || file.Contains("://", StringComparison.Ordinal) ? file : baseUri + file;

    private static string ExtensionOf(string requestUri)
    {
        var end = requestUri.IndexOfAny(['?', '#']);
        var path = end >= 0 ? requestUri[..end] : requestUri;
        return Path.GetExtension(path);
    }

    private static TranslationFormatRegistry BuildRegistry()
    {
        var registry = new TranslationFormatRegistry();
        registry.Register(new ArbTranslationFormat());
        registry.Register(new XliffTranslationFormat());
        registry.Register(new PoTranslationFormat());
        return registry;
    }
}
