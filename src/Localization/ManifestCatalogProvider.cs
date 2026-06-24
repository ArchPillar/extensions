using System.Globalization;
using System.Text.Json;
using ArchPillar.Extensions.Localization.Formats;
using ArchPillar.Extensions.Localization.Internal;

namespace ArchPillar.Extensions.Localization;

/// <summary>
/// An <see cref="ICatalogProvider"/> over an HTTP-served catalog manifest — the client-side counterpart to the
/// directory provider, for a host with no readable file system such as Blazor WebAssembly. It is created through
/// <see cref="CreateAsync"/>, which fetches the build-emitted manifest index once and builds a
/// <see cref="CatalogDescriptor"/> per listed catalog; the resulting provider is born ready, listing those
/// descriptors synchronously. Each descriptor's bytes are fetched over HTTP, so its <see cref="CatalogSource"/> is
/// <see cref="CatalogSource.Asynchronous"/> — the store loads it ahead of a lookup, never from inside one. The
/// manifest has no change signal, so <see cref="Watch"/> is a no-op; to refresh, recreate via
/// <see cref="CreateAsync"/>. A missing or malformed manifest lists nothing, leaving the app on its in-code
/// defaults.
/// </summary>
public sealed class ManifestCatalogProvider : ICatalogProvider
{
    /// <summary>The manifest file name the build emits and the provider reads.</summary>
    public const string DefaultManifestFileName = "apl-catalogs.json";

    /// <summary>
    /// The default manifest location, relative to the client's <see cref="HttpClient.BaseAddress"/>. The build
    /// writes this index beside the catalogs (and regenerates it after the publish-time merge), so the one path
    /// resolves the right files in both the development and the published layout.
    /// </summary>
    public const string DefaultManifestPath = "Translations/" + DefaultManifestFileName;

    private readonly string? _sourceCulture;

    private ManifestCatalogProvider(IReadOnlyList<CatalogDescriptor> catalogs, string? sourceCulture)
    {
        Catalogs = catalogs;
        _sourceCulture = sourceCulture;
    }

    /// <summary>
    /// Creates a <see cref="ManifestCatalogProvider"/> by fetching the manifest index at
    /// <paramref name="manifestUri"/> over <paramref name="httpClient"/> once and building the descriptor set —
    /// the async discovery a constructor cannot do. The returned provider is born ready.
    /// </summary>
    /// <param name="httpClient">The client used to fetch the manifest and catalogs; its base address resolves a relative URI.</param>
    /// <param name="manifestUri">The manifest URI, relative to the client's base address, or absolute. Defaults to <see cref="DefaultManifestPath"/>.</param>
    /// <param name="sourceCulture">
    /// The source language, listed by <see cref="CatalogsFor(CultureInfo)"/> alongside the requested culture so
    /// its genuine overrides are available; <see langword="null"/> to scope a per-culture listing to the
    /// requested culture and its parents only.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the fetch.</param>
    /// <returns>A born-ready provider listing the manifest's catalogs.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="httpClient"/> or <paramref name="manifestUri"/> is <see langword="null"/>.</exception>
    public static async Task<ManifestCatalogProvider> CreateAsync(
        HttpClient httpClient,
        string manifestUri = DefaultManifestPath,
        string? sourceCulture = null,
        CancellationToken cancellationToken = default)
    {
        if (httpClient is null)
        {
            throw new ArgumentNullException(nameof(httpClient));
        }

        if (manifestUri is null)
        {
            throw new ArgumentNullException(nameof(manifestUri));
        }

        TranslationFormatRegistry registry = BuiltInTranslationFormats.CreateRegistry();
        IReadOnlyList<string> uris = await ReadManifestAsync(httpClient, manifestUri, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<CatalogDescriptor> catalogs = Describe(httpClient, registry, uris);
        return new ManifestCatalogProvider(catalogs, sourceCulture);
    }

    /// <inheritdoc />
    public IReadOnlyList<CatalogDescriptor> Catalogs { get; }

    /// <inheritdoc />
    public IReadOnlyList<CatalogDescriptor> CatalogsFor(CultureInfo culture)
    {
        if (culture is null)
        {
            throw new ArgumentNullException(nameof(culture));
        }

        HashSet<string> wanted = CultureChain(culture);
        if (!string.IsNullOrEmpty(_sourceCulture))
        {
            wanted.Add(_sourceCulture);
        }

        return [.. Catalogs.Where(descriptor => wanted.Contains(descriptor.Culture))];
    }

    /// <inheritdoc />
    public IDisposable Watch(Action<CatalogDescriptor> onChanged)
    {
        if (onChanged is null)
        {
            throw new ArgumentNullException(nameof(onChanged));
        }

        return NoOpWatch.Instance;
    }

    private static List<CatalogDescriptor> Describe(HttpClient httpClient, TranslationFormatRegistry registry, IReadOnlyList<string> uris)
    {
        var descriptors = new List<CatalogDescriptor>();
        foreach (var uri in uris)
        {
            var extension = ExtensionOf(uri);
            if (registry.ResolveByExtension(extension) is null)
            {
                continue;
            }

            var requestUri = uri;
            descriptors.Add(new CatalogDescriptor
            {
                Culture = CultureFromUri(uri),
                Format = extension,
                Name = uri,
                Source = new CatalogSource.Asynchronous(token => FetchAsync(httpClient, requestUri, token))
            });
        }

        return descriptors;
    }

    private static async ValueTask<Stream> FetchAsync(HttpClient httpClient, string requestUri, CancellationToken cancellationToken)
    {
        // Dispose the response on every path. The bytes are buffered into a caller-owned MemoryStream rather than
        // handing back the live response stream, so the HttpResponseMessage is not leaked while the caller reads —
        // a catalog is small, so buffering it is cheap.
        using HttpResponseMessage response = await httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return new MemoryStream(bytes);
    }

    private static async ValueTask<IReadOnlyList<string>> ReadManifestAsync(HttpClient httpClient, string manifestUri, CancellationToken cancellationToken)
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
                // A malformed manifest lists nothing rather than throwing during startup.
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

    // The culture and its parent chain (de-AT -> de), by name, for a culture-scoped listing.
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
}
