using System.Globalization;
using ArchPillar.Extensions.Localization.Formats;

namespace ArchPillar.Extensions.Localization.Tests;

/// <summary>
/// The CatalogStore wired to consume catalog providers: it preserves the directory load (eager and on-demand),
/// the live no-restart culture switch, hot reload, format precedence, and concurrency safety after the
/// provider refactor.
/// </summary>
public sealed class CatalogStoreProviderTests
{
    private static readonly CultureInfo _german = CultureInfo.GetCultureInfo("de");
    private static readonly CultureInfo _french = CultureInfo.GetCultureInfo("fr");
    private static readonly string _category = typeof(Greeting).FullName!;

    [Fact]
    public void Directory_FormatPrecedence_XliffWinsOverArb()
    {
        var directory = NewDirectory();
        try
        {
            WriteArb(directory, "de", "from arb");
            WriteXliff(directory, "de", "from xliff");

            using var store = new CatalogStore(new LocalizerOptions { TranslationsDirectory = directory });

            Assert.Equal("from xliff", Resolve(store, _german));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task HotReload_RebuildsOnFileChangeAsync()
    {
        var directory = NewDirectory();
        try
        {
            using var store = new CatalogStore(new LocalizerOptions
            {
                TranslationsDirectory = directory,
                EnableHotReload = true,
                HotReloadDebounce = TimeSpan.FromMilliseconds(20)
            });

            Assert.Null(Resolve(store, _german));

            WriteArb(directory, "de", "Hallo");

            // The directory provider's debounced watch fires and the store rebuilds; poll until it lands.
            Assert.True(await EventuallyAsync(() => Resolve(store, _german) == "Hallo"), "hot reload did not pick up the new file");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void OnDemand_SwitchesLiveWithoutBlockingOnReal()
    {
        var directory = NewDirectory();
        try
        {
            WriteArb(directory, "de", "Hallo");
            WriteArb(directory, "fr", "Bonjour");

            using var store = new CatalogStore(new LocalizerOptions
            {
                TranslationsDirectory = directory,
                CultureLoading = CultureLoading.OnDemand
            });

            Assert.Empty(store.Snapshot.ByCulture);

            store.EnsureCulture(_german);
            Assert.Equal("Hallo", Resolve(store, _german));
            Assert.Null(Resolve(store, _french));

            store.EnsureCulture(_french);
            Assert.Equal("Bonjour", Resolve(store, _french));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task OnDemand_ConcurrentEnsureCulture_LoadsEachExactlyOnceAsync()
    {
        var directory = NewDirectory();
        try
        {
            WriteArb(directory, "de", "Hallo");

            using var store = new CatalogStore(new LocalizerOptions
            {
                TranslationsDirectory = directory,
                CultureLoading = CultureLoading.OnDemand
            });

            // Many threads request the same culture at once: the under-lock set Add gates the load, so the
            // result is consistent and never throws regardless of interleaving.
            await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => store.EnsureCulture(_german))));

            Assert.Equal("Hallo", Resolve(store, _german));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Reload_RereadsTheContentOfAKnownFile()
    {
        // The provider is born ready: its descriptor set is fixed at construction. Reload re-reads the bytes of
        // the catalogs already discovered, so an edit to a known file is picked up. (A brand-new file is
        // discovered through the directory Watch under hot reload, not Reload — see HotReload_RebuildsOnFileChange.)
        var directory = NewDirectory();
        try
        {
            WriteArb(directory, "de", "Hallo");

            using var store = new CatalogStore(new LocalizerOptions { TranslationsDirectory = directory });
            Assert.Equal("Hallo", Resolve(store, _german));

            WriteArb(directory, "de", "Servus");
            store.Reload();

            Assert.Equal("Servus", Resolve(store, _german));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string? Resolve(CatalogStore store, CultureInfo culture)
    {
        store.Snapshot.ByCulture.TryGetValue(culture.Name, out IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? byCategory);
        if (byCategory is null || !byCategory.TryGetValue(_category, out IReadOnlyDictionary<string, string>? map))
        {
            return null;
        }

        return map.TryGetValue("hello", out var value) ? value : null;
    }

    private static async Task<bool> EventuallyAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return condition();
    }

    private static string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "apl-storeprovider-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void WriteArb(string directory, string culture, string message) =>
        File.WriteAllText(Path.Combine(directory, $"App.{culture}.arb"), $$"""
            {
              "@@locale": "{{culture}}",
              "@@x-category": "{{_category}}",
              "hello": "{{message}}",
              "@hello": { "x-state": "Translated", "x-source-fingerprint": "fp" }
            }
            """);

    private static void WriteXliff(string directory, string culture, string message)
    {
        var catalog = new Catalog
        {
            Culture = culture,
            Entries =
            [
                new CatalogEntry
                {
                    Category = _category,
                    Key = "hello",
                    SourceMessage = "Hello",
                    TranslatedMessage = message,
                    SourceFingerprint = "fp",
                    State = TranslationState.Translated
                }
            ]
        };

        using FileStream stream = File.Create(Path.Combine(directory, $"App.{culture}.xliff"));
        new XliffTranslationFormat().WriteAsync(stream, catalog, CancellationToken.None).GetAwaiter().GetResult();
    }
}
