using System.Globalization;
using System.Text.Json;
using ArchPillar.Extensions.Localization.Catalogs;
using ArchPillar.Extensions.Localization.Formats;
using ArchPillar.Extensions.Localization.Internal;

namespace ArchPillar.Extensions.Localization.Providers;

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
    /// <param name="formats">The formats to parse catalogs with; defaults to the built-in set (XLIFF, ARB, PO).</param>
    /// <param name="cancellationToken">A token to cancel the fetch.</param>
    /// <returns>A born-ready provider listing the manifest's catalogs.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="httpClient"/> or <paramref name="manifestUri"/> is <see langword="null"/>.</exception>
    public static async Task<ManifestCatalogProvider> CreateAsync(
        HttpClient httpClient,
        string manifestUri = DefaultManifestPath,
        string? sourceCulture = null,
        TranslationFormatRegistry? formats = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(manifestUri);

        TranslationFormatRegistry registry = formats ?? BuiltInTranslationFormats.CreateRegistry();
        IReadOnlyList<string> uris = await ReadManifestAsync(httpClient, manifestUri, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<CatalogDescriptor> catalogs = Describe(httpClient, registry, uris);
        return new ManifestCatalogProvider(catalogs, sourceCulture);
    }

    /// <inheritdoc />
    public IReadOnlyList<CatalogDescriptor> Catalogs { get; }

    /// <inheritdoc />
    public IReadOnlyList<CatalogDescriptor> CatalogsFor(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        HashSet<string> wanted = CultureNames(culture);
        if (!string.IsNullOrEmpty(_sourceCulture))
        {
            wanted.Add(_sourceCulture);
        }

        return [.. Catalogs.Where(descriptor => wanted.Contains(descriptor.Culture))];
    }

    /// <inheritdoc />
    public IDisposable Watch(Action<CatalogDescriptor> onChanged)
    {
        ArgumentNullException.ThrowIfNull(onChanged);

        return NoOpWatch.Instance;
    }

    private static List<CatalogDescriptor> Describe(HttpClient httpClient, TranslationFormatRegistry registry, IReadOnlyList<string> uris)
    {
        var descriptors = new List<CatalogDescriptor>();
        foreach (var uri in uris)
        {
            var extension = CatalogFileName.ExtensionOf(uri);
            ITranslationFormat? resolved = registry.ResolveByExtension(extension);
            if (resolved is null)
            {
                continue;
            }

            var requestUri = uri;
            ITranslationFormat format = resolved;
            descriptors.Add(new CatalogDescriptor
            {
                Culture = CatalogFileName.CultureOf(uri),
                Format = extension,
                Name = uri,
                Source = new CatalogSource.Asynchronous(token => FetchAndReadAsync(httpClient, requestUri, format, token))
            });
        }

        return descriptors;
    }

    private static async ValueTask<Catalog> FetchAndReadAsync(HttpClient httpClient, string requestUri, ITranslationFormat format, CancellationToken cancellationToken)
    {
        // Buffer the bytes asynchronously, then parse from memory — the provider owns the parse, so the store gets a
        // ready catalog. The response is disposed on every path; a catalog is small, so buffering it is cheap.
        using HttpResponseMessage response = await httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        using var stream = new MemoryStream(bytes);
        return format.Read(stream);
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
    private static HashSet<string> CultureNames(CultureInfo culture)
    {
        var chain = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CultureInfo current in CultureChain.Of(culture))
        {
            chain.Add(current.Name);
        }

        return chain;
    }

    // The manifest lists bare file names; each resolves against the manifest's own directory so the catalogs are
    // fetched from the same folder, regardless of where that folder is mounted.
    private static string ManifestBase(string manifestUri)
    {
        var slash = manifestUri.LastIndexOf('/');
        return slash >= 0 ? manifestUri[..(slash + 1)] : string.Empty;
    }

    // An absolute path or a full URL is taken as-is; a bare name resolves against the manifest's directory. Passing
    // absolute/cross-origin entries through is safe: the manifest is a build-emitted, same-origin asset, so anyone
    // able to rewrite it could already serve malicious same-origin catalogs — and in the browser the fetch is still
    // governed by CORS. There is no wider trust boundary to guard here.
    private static string Resolve(string baseUri, string file) =>
        file.StartsWith('/') || file.Contains("://", StringComparison.Ordinal) ? file : baseUri + file;
}
