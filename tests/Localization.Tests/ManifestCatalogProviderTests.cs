using System.Globalization;
using System.Net;
using System.Text;
using ArchPillar.Extensions.Localization.Formats;

namespace ArchPillar.Extensions.Localization.Tests;

/// <summary>
/// The manifest catalog provider: <see cref="ManifestCatalogProvider.CreateAsync"/> fetches the build-emitted
/// manifest over HTTP and turns each listed catalog into a descriptor whose source is
/// <see cref="CatalogSource.Asynchronous"/>, resolving the culture from the file name. The provider is born ready,
/// listing those descriptors synchronously. <see cref="ICatalogProvider.CatalogsFor"/> scopes to the requested
/// culture, its parents, and the configured source language. A missing or malformed manifest lists nothing.
/// <see cref="ICatalogProvider.Watch"/> is a no-op.
/// </summary>
public sealed class ManifestCatalogProviderTests : IDisposable
{
    private static readonly CultureInfo _german = CultureInfo.GetCultureInfo("de");
    private readonly List<IDisposable> _disposables = [];

    [Fact]
    public async Task CreateAsync_DescribesEveryCatalogInTheManifestAsync()
    {
        var de = await ArbBytesAsync("de", "greeting", "Hallo");
        const string Manifest = "{\"version\":1,\"catalogs\":["
            + "{\"culture\":\"de\",\"file\":\"App.de.arb\"},"
            + "{\"culture\":\"fr\",\"file\":\"App.fr.arb\"}]}";
        HttpClient http = NewClient(new()
        {
            ["/Translations/apl-catalogs.json"] = Ok(Encoding.UTF8.GetBytes(Manifest)),
            ["/Translations/App.de.arb"] = Ok(de)
        });

        ManifestCatalogProvider provider = await ManifestCatalogProvider.CreateAsync(http);

        Assert.Equal(2, provider.Catalogs.Count);
        Assert.Contains(provider.Catalogs, descriptor => descriptor.Culture == "de" && descriptor.Format == ".arb");
        Assert.Contains(provider.Catalogs, descriptor => descriptor.Culture == "fr");
    }

    [Fact]
    public async Task CreateAsync_DescriptorsCarryAnAsynchronousLoadAndListSynchronouslyAsync()
    {
        var de = await ArbBytesAsync("de", "greeting", "Hallo");
        const string Manifest = "{\"version\":1,\"catalogs\":[{\"culture\":\"de\",\"file\":\"App.de.arb\"}]}";
        HttpClient http = NewClient(new()
        {
            ["/Translations/apl-catalogs.json"] = Ok(Encoding.UTF8.GetBytes(Manifest)),
            ["/Translations/App.de.arb"] = Ok(de)
        });

        ManifestCatalogProvider provider = await ManifestCatalogProvider.CreateAsync(http);
        CatalogDescriptor descriptor = Assert.Single(provider.Catalogs);
        CatalogSource.Asynchronous asynchronous = Assert.IsType<CatalogSource.Asynchronous>(descriptor.Source);

        await using Stream stream = await asynchronous.OpenAsync(CancellationToken.None);
        Catalog catalog = new ArbTranslationFormat().Read(stream);

        Assert.Contains(catalog.Entries, entry => entry.TranslatedMessage == "Hallo");
    }

    [Fact]
    public async Task CatalogsFor_ScopesToTheCultureAndTheSourceAsync()
    {
        var de = await ArbBytesAsync("de", "greeting", "Hallo");
        const string Manifest = "{\"version\":1,\"catalogs\":["
            + "{\"culture\":\"de\",\"file\":\"App.de.arb\"},"
            + "{\"culture\":\"fr\",\"file\":\"App.fr.arb\"},"
            + "{\"culture\":\"en\",\"file\":\"App.en.arb\"}]}";
        HttpClient http = NewClient(new()
        {
            ["/Translations/apl-catalogs.json"] = Ok(Encoding.UTF8.GetBytes(Manifest)),
            ["/Translations/App.de.arb"] = Ok(de)
        });
        ManifestCatalogProvider provider = await ManifestCatalogProvider.CreateAsync(http, sourceCulture: "en");

        IReadOnlyList<CatalogDescriptor> scoped = provider.CatalogsFor(_german);

        // de (requested) and en (source) are in scope; fr is not.
        Assert.Equal(2, scoped.Count);
        Assert.Contains(scoped, descriptor => descriptor.Culture == "de");
        Assert.Contains(scoped, descriptor => descriptor.Culture == "en");
        Assert.DoesNotContain(scoped, descriptor => descriptor.Culture == "fr");
    }

    [Fact]
    public async Task CatalogsFor_WithoutASourceScopesToTheCultureChainOnlyAsync()
    {
        const string Manifest = "{\"version\":1,\"catalogs\":["
            + "{\"culture\":\"de\",\"file\":\"App.de.arb\"},"
            + "{\"culture\":\"en\",\"file\":\"App.en.arb\"}]}";
        HttpClient http = NewClient(new() { ["/Translations/apl-catalogs.json"] = Ok(Encoding.UTF8.GetBytes(Manifest)) });
        ManifestCatalogProvider provider = await ManifestCatalogProvider.CreateAsync(http);

        IReadOnlyList<CatalogDescriptor> scoped = provider.CatalogsFor(_german);

        CatalogDescriptor descriptor = Assert.Single(scoped);
        Assert.Equal("de", descriptor.Culture);
    }

    [Fact]
    public async Task CreateAsync_MissingManifestListsNothingAsync()
    {
        HttpClient http = NewClient(new() { ["/Translations/apl-catalogs.json"] = NotFound() });

        ManifestCatalogProvider provider = await ManifestCatalogProvider.CreateAsync(http);

        Assert.Empty(provider.Catalogs);
    }

    [Fact]
    public async Task CreateAsync_MalformedManifestListsNothingAsync()
    {
        HttpClient http = NewClient(new() { ["/Translations/apl-catalogs.json"] = Ok(Encoding.UTF8.GetBytes("{ not valid json")) });

        ManifestCatalogProvider provider = await ManifestCatalogProvider.CreateAsync(http);

        Assert.Empty(provider.Catalogs);
    }

    [Fact]
    public async Task CreateAsync_UnknownExtensionIsSkippedAsync()
    {
        const string Manifest = "{\"version\":1,\"catalogs\":[{\"culture\":\"de\",\"file\":\"App.de.txt\"}]}";
        HttpClient http = NewClient(new() { ["/Translations/apl-catalogs.json"] = Ok(Encoding.UTF8.GetBytes(Manifest)) });

        ManifestCatalogProvider provider = await ManifestCatalogProvider.CreateAsync(http);

        Assert.Empty(provider.Catalogs);
    }

    [Fact]
    public async Task Watch_IsANoOpHandleAsync()
    {
        using var http = new HttpClient { BaseAddress = new Uri("http://localhost/") };
        ManifestCatalogProvider provider = await ManifestCatalogProvider.CreateAsync(http);

        using IDisposable handle = provider.Watch(_ => { });

        Assert.NotNull(handle);
    }

    [Fact]
    public async Task CreateAsync_NullArgumentsThrowAsync()
    {
        using var http = new HttpClient { BaseAddress = new Uri("http://localhost/") };

        await Assert.ThrowsAsync<ArgumentNullException>(() => ManifestCatalogProvider.CreateAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => ManifestCatalogProvider.CreateAsync(http, null!));
    }

    public void Dispose()
    {
        foreach (IDisposable disposable in _disposables)
        {
            disposable.Dispose();
        }
    }

    private static async Task<byte[]> ArbBytesAsync(string culture, string key, string translated)
    {
        var catalog = new Catalog
        {
            Culture = culture,
            Entries =
            [
                new CatalogEntry
                {
                    Key = key,
                    SourceMessage = "Hello",
                    TranslatedMessage = translated,
                    SourceFingerprint = "fp",
                    State = TranslationState.Translated
                }
            ]
        };

        using var buffer = new MemoryStream();
        await new ArbTranslationFormat().WriteAsync(buffer, catalog, CancellationToken.None);
        return buffer.ToArray();
    }

    private HttpClient NewClient(Dictionary<string, (HttpStatusCode Status, byte[] Body)> responses)
    {
        var handler = new StubHandler(responses);
        var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://localhost/") };
        _disposables.Add(handler);
        _disposables.Add(client);
        return client;
    }

    private static (HttpStatusCode, byte[]) Ok(byte[] body) => (HttpStatusCode.OK, body);

    private static (HttpStatusCode, byte[]) NotFound() => (HttpStatusCode.NotFound, []);

    // A stub handler over a fixed response table. It yields asynchronously (await Task.Yield()) so the response
    // completes asynchronously like a real HttpClient — keeping a manifest provider genuinely async, so the
    // store's synchronous on-demand path skips it and it is loaded only through the awaited paths.
    internal sealed class StubHandler(Dictionary<string, (HttpStatusCode Status, byte[] Body)> responses) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Yield();
            var path = request.RequestUri!.AbsolutePath;
            if (responses.TryGetValue(path, out (HttpStatusCode Status, byte[] Body) entry))
            {
                return new HttpResponseMessage(entry.Status) { Content = new ByteArrayContent(entry.Body) };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
