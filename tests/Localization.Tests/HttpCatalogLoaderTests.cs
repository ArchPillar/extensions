using System.Globalization;
using System.Net;
using System.Text;
using ArchPillar.Extensions.Localization.Formats;

namespace ArchPillar.Extensions.Localization.Tests;

/// <summary>
/// The HTTP catalog loader — the client-side counterpart to the directory source. Catalogs are fetched as
/// static assets and layered in, discovery runs through a build-emitted manifest, and a missing or malformed
/// asset is skipped so the app degrades to its in-code defaults rather than throwing.
/// </summary>
public sealed class HttpCatalogLoaderTests : IDisposable
{
    private static readonly CultureInfo _german = CultureInfo.GetCultureInfo("de");
    private readonly List<IDisposable> _disposables = [];

    [Fact]
    public async Task AddCatalogsFromHttp_FetchesAndResolvesAsync()
    {
        var arb = await ArbBytesAsync("de", "greeting", "Hallo");
        HttpClient http = NewClient(new() { ["/Translations/App.de.arb"] = Ok(arb) });
        using var context = new LocalizationContext(new LocalizerOptions { SourceCulture = "en" });

        var loaded = await context.AddCatalogsFromHttpAsync(http, ["Translations/App.de.arb"]);

        Assert.Equal(1, loaded);
        WithCulture(_german, () => Assert.Equal("Hallo", context.Default.Translate("greeting", "Hello")));
    }

    [Fact]
    public async Task AddCatalogsFromHttp_MissingCatalogIsSkippedAsync()
    {
        HttpClient http = NewClient(new() { ["/Translations/App.de.arb"] = NotFound() });
        using var context = new LocalizationContext(new LocalizerOptions { SourceCulture = "en" });

        var loaded = await context.AddCatalogsFromHttpAsync(http, ["Translations/App.de.arb"]);

        Assert.Equal(0, loaded);
        WithCulture(_german, () => Assert.Equal("Hello", context.Default.Translate("greeting", "Hello")));
    }

    [Fact]
    public async Task AddCatalogsFromHttp_MalformedCatalogIsSkippedAsync()
    {
        HttpClient http = NewClient(new() { ["/Translations/App.de.arb"] = Ok(Encoding.UTF8.GetBytes("{ not valid arb")) });
        using var context = new LocalizationContext(new LocalizerOptions { SourceCulture = "en" });

        var loaded = await context.AddCatalogsFromHttpAsync(http, ["Translations/App.de.arb"]);

        Assert.Equal(0, loaded);
    }

    [Fact]
    public async Task AddCatalogsFromHttp_UnknownExtensionIsSkippedWithoutFetchingAsync()
    {
        HttpClient http = NewClient([]);
        using var context = new LocalizationContext(new LocalizerOptions { SourceCulture = "en" });

        var loaded = await context.AddCatalogsFromHttpAsync(http, ["Translations/App.de.txt"]);

        Assert.Equal(0, loaded);
    }

    [Fact]
    public async Task AddCatalogsFromManifest_DiscoversThroughManifestAsync()
    {
        var arb = await ArbBytesAsync("de", "greeting", "Hallo");
        const string Manifest = "{\"version\":1,\"catalogs\":[{\"culture\":\"de\",\"file\":\"App.de.arb\"}]}";
        HttpClient http = NewClient(new()
        {
            ["/Translations/apl-catalogs.json"] = Ok(Encoding.UTF8.GetBytes(Manifest)),
            ["/Translations/App.de.arb"] = Ok(arb)
        });
        using var context = new LocalizationContext(new LocalizerOptions { SourceCulture = "en" });

        var loaded = await context.AddCatalogsFromManifestAsync(http);

        Assert.Equal(1, loaded);
        WithCulture(_german, () => Assert.Equal("Hallo", context.Default.Translate("greeting", "Hello")));
    }

    [Fact]
    public async Task AddCatalogsFromManifest_MissingManifestLoadsNothingAsync()
    {
        HttpClient http = NewClient(new() { ["/Translations/apl-catalogs.json"] = NotFound() });
        using var context = new LocalizationContext(new LocalizerOptions { SourceCulture = "en" });

        var loaded = await context.AddCatalogsFromManifestAsync(http);

        Assert.Equal(0, loaded);
    }

    [Fact]
    public async Task AddCatalogsFromHttp_NullArgumentsThrowAsync()
    {
        using var context = new LocalizationContext(new LocalizerOptions());
        using var http = new HttpClient();

        await Assert.ThrowsAsync<ArgumentNullException>(() => context.AddCatalogsFromHttpAsync(null!, ["App.de.arb"]));
        await Assert.ThrowsAsync<ArgumentNullException>(() => context.AddCatalogsFromHttpAsync(http, null!));
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

    // The handler and client are owned by the fixture and disposed together, so the loader is exercised end to
    // end against a real HttpClient with no live server.
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

    private static void WithCulture(CultureInfo culture, Action action)
    {
        CultureInfo original = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = culture;
        try
        {
            action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    private sealed class StubHandler(Dictionary<string, (HttpStatusCode Status, byte[] Body)> responses) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            var path = request.RequestUri!.AbsolutePath;
            if (responses.TryGetValue(path, out (HttpStatusCode Status, byte[] Body) entry))
            {
                return new HttpResponseMessage(entry.Status) { Content = new ByteArrayContent(entry.Body) };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
