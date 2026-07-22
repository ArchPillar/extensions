using System.Globalization;
using ArchPillar.Extensions.Localization.Catalogs;
using ArchPillar.Extensions.Localization.Formats;
using ArchPillar.Extensions.Localization.Internal;
using ArchPillar.Extensions.Localization.Providers;

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

            Assert.Empty(store.LoadedCultures);

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
        // A gated asynchronous provider so the single background fetch stays in flight for the whole burst: every
        // overlapping request for the same not-yet-loaded culture then coalesces onto that one in-flight task
        // instead of racing a second fetch. (The pre-fix duplicate fetch is a thread race and only fires
        // intermittently, so this pins the post-fix invariant — one fetch per identity — as a regression guard
        // rather than a deterministic RED; the fix is correctness-by-construction via Lazy<Task>.)
        var provider = new GatedCountingProvider("de", "Hallo");
        using var store = new CatalogStore(new LocalizerOptions
        {
            // A directory that does not exist, so the auto-wired directory provider contributes nothing and the
            // test drives the store purely through the gated provider.
            TranslationsDirectory = Path.Combine(Path.GetTempPath(), "apl-nonexistent-" + Guid.NewGuid().ToString("N")),
            SourceCulture = "en",
            CultureLoading = CultureLoading.OnDemand,
            Providers = [_ => provider]
        });

        // The fire-and-forget miss queues the single background fetch (held at the gate); the awaited loads then
        // pile onto the same in-flight identity. Each LoadCultureAsync reaches the loader's dedup synchronously, so
        // with the fetch held they all coalesce before any of them can complete.
        store.EnsureCulture(_german);
        var loads = Enumerable.Range(0, 32)
            .Select(_ => store.LoadCultureAsync(_german, CancellationToken.None))
            .ToList();

        provider.Release();
        await Task.WhenAll(loads).WaitAsync(TimeSpan.FromSeconds(5));

        // One fetch per identity: the coalesced in-flight task opened the provider exactly once despite the burst.
        Assert.Equal(1, provider.OpenCount);
        Assert.Equal("Hallo", Resolve(store, _german));
    }

    private static string? Resolve(CatalogStore store, CultureInfo culture) =>
        store.Lookup(culture, _category, "hello");

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
        new XliffTranslationFormat().WriteAsync(stream, catalog).GetAwaiter().GetResult();
    }

    // An asynchronous provider that holds every fetch at a caller-released gate and counts how many times a catalog
    // was opened, so a test can pile overlapping loads onto one identity and assert they coalesced to a single fetch.
    private sealed class GatedCountingProvider : ICatalogProvider
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _openCount;

        public GatedCountingProvider(string culture, string message)
        {
            Catalogs =
            [
                new CatalogDescriptor
                {
                    Culture = culture,
                    Format = "arb",
                    Name = culture + ".arb",
                    Source = new CatalogSource.Asynchronous(async _ =>
                    {
                        Interlocked.Increment(ref _openCount);
                        await _gate.Task.ConfigureAwait(false);
                        return CatalogFor(culture, message);
                    })
                }
            ];
        }

        public IReadOnlyList<CatalogDescriptor> Catalogs { get; }

        public int OpenCount => Volatile.Read(ref _openCount);

        public void Release() => _gate.TrySetResult();

        public IReadOnlyList<CatalogDescriptor> CatalogsFor(CultureInfo culture) =>
        [
            .. Catalogs.Where(descriptor => string.Equals(descriptor.Culture, culture.Name, StringComparison.OrdinalIgnoreCase))
        ];

        public IDisposable Watch(Action<CatalogDescriptor> onChanged) => NoOpWatch.Instance;

        private static Catalog CatalogFor(string culture, string message) => new()
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
    }
}
