using System.Globalization;
using System.Text;

namespace ArchPillar.Extensions.Localization.Tests;

/// <summary>
/// The catalog store's two load paths under the sync/async redesign. A synchronous provider's culture loads
/// inline on the sync path (<see cref="CatalogStore.EnsureCulture"/>) and resolves immediately; an asynchronous
/// provider is never opened on the sync path — a miss returns the in-code default and a background load lands
/// later through <see cref="CatalogStore.CatalogsChanged"/>, while the awaited paths
/// (<see cref="CatalogStore.LoadCultureAsync"/>, <see cref="CatalogStore.PreloadAllAsync"/>) load with no flash.
/// A failed catalog is dropped and not retried, and a <see cref="ICatalogProvider.Watch"/> signal force-reloads.
/// </summary>
public sealed class CatalogStoreTests
{
    private const string Category = "Greeting";
    private static readonly CultureInfo _german = CultureInfo.GetCultureInfo("de");
    private static readonly CultureInfo _french = CultureInfo.GetCultureInfo("fr");

    [Fact]
    public void EnsureCulture_SynchronousProvider_LoadsInlineAndResolvesImmediately()
    {
        using CatalogStore store = EmptyStore(CultureLoading.OnDemand);
        store.AddProvider(new StubProvider(Synchronous("de", "Hallo")));

        Assert.Null(Resolve(store, _german));

        store.EnsureCulture(_german);

        // The synchronous provider's culture is loaded inline, so the very next lookup resolves it.
        Assert.Equal("Hallo", Resolve(store, _german));
    }

    [Fact]
    public async Task EnsureCulture_AsynchronousOnlyCulture_ReturnsDefaultThenResolvesAfterCatalogsChangedAsync()
    {
        using CatalogStore store = EmptyStore(CultureLoading.OnDemand);
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.CatalogsChanged += () => changed.TrySetResult();

        // Hold the background fetch at a gate so the "still default" state below is observable deterministically:
        // without it the queued load can commit before the assertion and the test flakes.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.AddProvider(new StubProvider(GatedAsynchronous("de", "Hallo", gate.Task)));

        // The synchronous miss must not block on the network: it returns nothing now and queues a background load
        // (parked at the gate, so it cannot have committed yet).
        store.EnsureCulture(_german);
        Assert.Null(Resolve(store, _german));

        // Release the fetch; the background load lands and raises CatalogsChanged; after that the lookup resolves.
        gate.SetResult();
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Hallo", Resolve(store, _german));
    }

    [Fact]
    public async Task LoadCultureAsync_AsynchronousProvider_ResolvesWithNoFlashAsync()
    {
        using CatalogStore store = EmptyStore(CultureLoading.OnDemand);
        store.AddProvider(new StubProvider(Asynchronous("de", "Hallo")));

        await store.LoadCultureAsync(_german, CancellationToken.None);

        // Awaited: the culture is already in the snapshot when the synchronous lookup runs — no default flash.
        Assert.Equal("Hallo", Resolve(store, _german));
    }

    [Fact]
    public async Task PreloadAllAsync_LoadsAllKnownCulturesAsync()
    {
        using CatalogStore store = EmptyStore(CultureLoading.OnDemand);
        store.AddProvider(new StubProvider(Asynchronous("de", "Hallo"), Asynchronous("fr", "Bonjour")));

        await store.PreloadAllAsync(CancellationToken.None);

        Assert.Equal("Hallo", Resolve(store, _german));
        Assert.Equal("Bonjour", Resolve(store, _french));
    }

    [Fact]
    public void EnsureCulture_MalformedCatalog_IsMarkedFailedAndNotRetried()
    {
        using CatalogStore store = EmptyStore(CultureLoading.OnDemand);
        var provider = new StubProvider(Malformed("de"));
        store.AddProvider(provider);

        store.EnsureCulture(_german);

        // The malformed catalog is dropped, so the lookup falls back to nothing (the in-code default at the localizer).
        Assert.Null(Resolve(store, _german));
        Assert.Equal(1, provider.OpenCount);

        // A subsequent lookup of the same culture does not re-open the failed catalog — it is not retried.
        store.EnsureCulture(_german);
        Assert.Equal(1, provider.OpenCount);
    }

    [Fact]
    public void Eager_LoadsAllEnumerableCulturesAtStartup()
    {
        using CatalogStore store = EmptyStore(CultureLoading.Eager);
        store.AddProvider(new StubProvider(Synchronous("de", "Hallo"), Synchronous("fr", "Bonjour")));

        // Eager ingests every enumerable catalog at AddProvider time — no one had to request a culture.
        Assert.Equal("Hallo", Resolve(store, _german));
        Assert.Equal("Bonjour", Resolve(store, _french));
    }

    [Fact]
    public void OnDemand_LoadsNothingUntilRequested()
    {
        using CatalogStore store = EmptyStore(CultureLoading.OnDemand);
        store.AddProvider(new StubProvider(Synchronous("de", "Hallo")));

        // On-demand ingests nothing up front.
        Assert.Empty(store.Snapshot.ByCulture);

        store.EnsureCulture(_german);
        Assert.Equal("Hallo", Resolve(store, _german));
    }

    [Fact]
    public void DefaultCultureLoading_IsPlatformDerived()
    {
        // The platform default: on-demand in the browser (Blazor WebAssembly), eager elsewhere.
        CultureLoading expected = OperatingSystem.IsBrowser() ? CultureLoading.OnDemand : CultureLoading.Eager;

        Assert.Equal(expected, new LocalizerOptions().CultureLoading);
    }

    [Fact]
    public void Watch_FiresEditedDescriptor_UpdatesSnapshotAndRaisesCatalogsChanged()
    {
        using var store = new CatalogStore(new LocalizerOptions
        {
            SourceCulture = "en",
            CultureLoading = CultureLoading.Eager,
            EnableHotReload = true
        });
        var watchable = new WatchableProvider(Synchronous("de", "Hallo"));
        store.AddProvider(watchable);
        Assert.Equal("Hallo", Resolve(store, _german));

        var changedRaised = false;
        store.CatalogsChanged += () => changedRaised = true;

        // The provider signals that the de catalog changed, carrying an edited descriptor: the store force-reloads
        // just that catalog and raises CatalogsChanged.
        watchable.Fire(Synchronous("de", "Servus"));

        Assert.Equal("Servus", Resolve(store, _german));
        Assert.True(changedRaised);
    }

    private static CatalogStore EmptyStore(CultureLoading loading) =>
        new(new LocalizerOptions
        {
            // A directory that does not exist, so the auto-wired directory provider contributes nothing and the
            // test drives the store purely through the stub provider it adds.
            TranslationsDirectory = Path.Combine(Path.GetTempPath(), "apl-empty-" + Guid.NewGuid().ToString("N")),
            SourceCulture = "en",
            CultureLoading = loading
        });

    private static string? Resolve(CatalogStore store, CultureInfo culture)
    {
        store.Snapshot.ByCulture.TryGetValue(culture.Name, out IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? byCategory);
        if (byCategory is null || !byCategory.TryGetValue(Category, out IReadOnlyDictionary<string, string>? map))
        {
            return null;
        }

        return map.TryGetValue("hello", out var value) ? value : null;
    }

    private static byte[] ArbBytes(string culture, string message) => Encoding.UTF8.GetBytes($$"""
        {
          "@@locale": "{{culture}}",
          "@@x-category": "{{Category}}",
          "hello": "{{message}}",
          "@hello": { "x-state": "Translated", "x-source-fingerprint": "fp" }
        }
        """);

    private static CatalogSpec Synchronous(string culture, string message) =>
        new(culture, () => new CatalogSource.Synchronous(() => new MemoryStream(ArbBytes(culture, message))));

    private static CatalogSpec Asynchronous(string culture, string message) =>
        new(culture, () => new CatalogSource.Asynchronous(async _ =>
        {
            // Yield so the load genuinely completes asynchronously, like a real network fetch — the store's
            // synchronous lookup path must skip it rather than blocking.
            await Task.Yield();
            return new MemoryStream(ArbBytes(culture, message));
        }));

    // An asynchronous load held at a caller-controlled gate, so a test can assert the pre-load state before the
    // background load is allowed to commit. The gate stands in for a slow network fetch, deterministically.
    private static CatalogSpec GatedAsynchronous(string culture, string message, Task gate) =>
        new(culture, () => new CatalogSource.Asynchronous(async _ =>
        {
            await gate.ConfigureAwait(false);
            return new MemoryStream(ArbBytes(culture, message));
        }));

    private static CatalogSpec Malformed(string culture) =>
        new(culture, () => new CatalogSource.Synchronous(() => new MemoryStream(Encoding.UTF8.GetBytes("{ not valid arb"))));

    // A descriptor recipe: the culture and a factory for its load, plus an open counter so a test can assert how
    // many times the bytes were opened (fail-no-retry).
    private sealed class CatalogSpec(string culture, Func<CatalogSource> load)
    {
        public string Culture { get; } = culture;

        public int OpenCount { get; private set; }

        public CatalogDescriptor Describe() => new()
        {
            Culture = Culture,
            Format = "arb",
            Name = Culture + ".arb",
            Source = Wrap(load())
        };

        private CatalogSource Wrap(CatalogSource inner) => inner switch
        {
            CatalogSource.Synchronous sync => new CatalogSource.Synchronous(() =>
            {
                OpenCount++;
                return sync.Open();
            }),
            CatalogSource.Asynchronous asynchronous => new CatalogSource.Asynchronous(token =>
            {
                OpenCount++;
                return asynchronous.OpenAsync(token);
            }),
            _ => inner
        };
    }

    // A born-ready stub provider over a fixed set of descriptor recipes, exposing the total open count across them.
    private sealed class StubProvider : ICatalogProvider
    {
        private readonly CatalogSpec[] _specs;

        public StubProvider(params CatalogSpec[] specs)
        {
            _specs = specs;
            Catalogs = [.. specs.Select(spec => spec.Describe())];
        }

        public IReadOnlyList<CatalogDescriptor> Catalogs { get; }

        public int OpenCount => _specs.Sum(spec => spec.OpenCount);

        public IReadOnlyList<CatalogDescriptor> CatalogsFor(CultureInfo culture) =>
        [
            .. Catalogs.Where(descriptor => string.Equals(descriptor.Culture, culture.Name, StringComparison.OrdinalIgnoreCase))
        ];

        public IDisposable Watch(Action<CatalogDescriptor> onChanged) => NoOpWatch.Instance;

        private sealed class NoOpWatch : IDisposable
        {
            public static readonly NoOpWatch Instance = new();

            public void Dispose()
            {
            }
        }
    }

    // A provider whose Watch hands back a callback the test can fire with an edited descriptor, modelling a file
    // edit or a satellite-assembly load arriving after construction.
    private sealed class WatchableProvider : ICatalogProvider
    {
        private Action<CatalogDescriptor>? _onChanged;

        public WatchableProvider(CatalogSpec initial)
        {
            Catalogs = [initial.Describe()];
        }

        public IReadOnlyList<CatalogDescriptor> Catalogs { get; }

        public IReadOnlyList<CatalogDescriptor> CatalogsFor(CultureInfo culture) =>
        [
            .. Catalogs.Where(descriptor => string.Equals(descriptor.Culture, culture.Name, StringComparison.OrdinalIgnoreCase))
        ];

        public IDisposable Watch(Action<CatalogDescriptor> onChanged)
        {
            _onChanged = onChanged;
            return new Subscription(this);
        }

        public void Fire(CatalogSpec edited) => _onChanged?.Invoke(edited.Describe());

        private sealed class Subscription(WatchableProvider provider) : IDisposable
        {
            public void Dispose() => provider._onChanged = null;
        }
    }
}
