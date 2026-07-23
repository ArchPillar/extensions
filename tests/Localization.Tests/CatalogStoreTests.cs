using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using ArchPillar.Extensions.Localization.Catalogs;
using ArchPillar.Extensions.Localization.Formats;
using ArchPillar.Extensions.Localization.Providers;

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
    private const string FaultSentinel = "unobserved-guard-fault";
    private static readonly CultureInfo _german = CultureInfo.GetCultureInfo("de");
    private static readonly CultureInfo _french = CultureInfo.GetCultureInfo("fr");

    [Fact]
    public void EnsureCulture_SynchronousProvider_LoadsInlineAndResolvesImmediately()
    {
        using CatalogStore store = StoreWith(CultureLoading.OnDemand, new StubProvider(Synchronous("de", "Hallo")));

        Assert.Null(Resolve(store, _german));

        store.EnsureCulture(_german);

        // The synchronous provider's culture is loaded inline, so the very next lookup resolves it.
        Assert.Equal("Hallo", Resolve(store, _german));
    }

    [Fact]
    public async Task EnsureCulture_AsynchronousOnlyCulture_ReturnsDefaultThenResolvesAfterCatalogsChangedAsync()
    {
        // Hold the background fetch at a gate so the "still default" state below is observable deterministically:
        // without it the queued load can commit before the assertion and the test flakes.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new StubProvider(GatedAsynchronous("de", "Hallo", gate.Task));
        using CatalogStore store = StoreWith(CultureLoading.OnDemand, provider);
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.CatalogsChanged += () => changed.TrySetResult();

        // Nothing is opened until a culture is requested.
        Assert.Equal(0, provider.OpenCount);

        // The synchronous miss must not block on the network: it returns nothing now and queues a background load
        // (parked at the gate, so it cannot have committed yet). The async source is opened on that background path
        // only — never inline on the lookup — and exactly once.
        store.EnsureCulture(_german);
        Assert.Null(Resolve(store, _german));
        Assert.Equal(1, provider.OpenCount);

        // Release the fetch; the background load lands and raises CatalogsChanged; after that the lookup resolves.
        gate.SetResult();
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Hallo", Resolve(store, _german));
    }

    [Fact]
    public async Task LoadCultureAsync_AsynchronousProvider_ResolvesWithNoFlashAsync()
    {
        using CatalogStore store = StoreWith(CultureLoading.OnDemand, new StubProvider(Asynchronous("de", "Hallo")));

        await store.LoadCultureAsync(_german, CancellationToken.None);

        // Awaited: the culture is already in the snapshot when the synchronous lookup runs — no default flash.
        Assert.Equal("Hallo", Resolve(store, _german));
    }

    [Fact]
    public async Task PreloadAllAsync_LoadsAllKnownCulturesAsync()
    {
        using CatalogStore store = StoreWith(CultureLoading.OnDemand, new StubProvider(Asynchronous("de", "Hallo"), Asynchronous("fr", "Bonjour")));

        await store.PreloadAllAsync(CancellationToken.None);

        Assert.Equal("Hallo", Resolve(store, _german));
        Assert.Equal("Bonjour", Resolve(store, _french));
    }

    [Fact]
    public void EnsureCulture_MalformedCatalog_IsMarkedFailedAndNotRetried()
    {
        var provider = new StubProvider(Malformed("de"));
        using CatalogStore store = StoreWith(CultureLoading.OnDemand, provider);

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
        using CatalogStore store = StoreWith(CultureLoading.Eager, new StubProvider(Synchronous("de", "Hallo"), Synchronous("fr", "Bonjour")));

        // Eager ingests every enumerable catalog at startup — no one had to request a culture.
        Assert.Equal("Hallo", Resolve(store, _german));
        Assert.Equal("Bonjour", Resolve(store, _french));
    }

    [Fact]
    public void OnDemand_LoadsNothingUntilRequested()
    {
        using CatalogStore store = StoreWith(CultureLoading.OnDemand, new StubProvider(Synchronous("de", "Hallo")));

        // On-demand ingests nothing up front.
        Assert.Empty(store.LoadedCultures);

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
        var watchable = new WatchableProvider(Synchronous("de", "Hallo"));
        using var store = new CatalogStore(new LocalizerOptions
        {
            SourceCulture = "en",
            CultureLoading = CultureLoading.Eager,
            EnableHotReload = true,
            Providers = [_ => watchable]
        });
        Assert.Equal("Hallo", Resolve(store, _german));

        var changedRaised = false;
        store.CatalogsChanged += () => changedRaised = true;

        // The provider signals that the de catalog changed, carrying an edited descriptor: the store force-reloads
        // just that catalog and raises CatalogsChanged.
        watchable.Fire(Synchronous("de", "Servus"));

        Assert.Equal("Servus", Resolve(store, _german));
        Assert.True(changedRaised);
    }

    [Fact]
    public void Configure_OnDemand_ReloadsInUseCulturesAgainstTheRebuiltProviders()
    {
        // On-demand, so a reconfigure reloads the in-use culture only through StartCore's per-culture probe (eager
        // would reload it through the whole inventory instead). The provider survives because it comes from options.
        var options = new LocalizerOptions
        {
            TranslationsDirectory = Path.Combine(Path.GetTempPath(), "apl-empty-" + Guid.NewGuid().ToString("N")),
            SourceCulture = "en",
            CultureLoading = CultureLoading.OnDemand,
            Providers = [_ => new StubProvider(Synchronous("de", "Hallo"))]
        };
        using var store = new CatalogStore(options);
        store.EnsureCulture(_german);
        Assert.Equal("Hallo", Resolve(store, _german));

        // A reconfigure rebuilds the provider states from scratch but keeps the in-use culture set, so de must
        // reload — otherwise its retained "in use" mark makes EnsureCulture's fast path skip it and it resolves empty.
        store.Configure(options);

        Assert.Equal("Hallo", Resolve(store, _german));
    }

    [Fact]
    public void Reset_WipesSnapshotAndRaisesCatalogsChanged()
    {
        using CatalogStore store = StoreWith(CultureLoading.Eager, new StubProvider(Synchronous("de", "Hallo")));
        Assert.Equal("Hallo", Resolve(store, _german));

        var changedRaised = false;
        store.CatalogsChanged += () => changedRaised = true;

        store.Reset();

        // Reset drops the configured provider and rebuilds an empty snapshot: subscribers must be notified of that
        // populated→empty transition rather than left holding the stale "Hallo" snapshot.
        Assert.True(changedRaised, "Reset must raise CatalogsChanged for the populated→empty transition");

        // And the override no longer resolves — the store falls back to the in-code default (null here).
        Assert.Null(Resolve(store, _german));
    }

    [Fact]
    public void EnsureCulture_FaultingAsyncProvider_DegradesToDefaultAndStaysUsable()
    {
        using CatalogStore store = StoreWith(CultureLoading.OnDemand, new StubProvider(FaultingAsynchronous("de")));

        // A non-catalog-load-failure (InvalidOperationException, outside the loader's caught set) faults the async
        // load on the fire-and-forget miss path: FetchAsync lets it propagate and the discarded loader task faults.
        // The store must not crash, and the miss degrades to the in-code default (null here). This asserts graceful
        // degradation and continued usability; the companion test below proves the fault-observing continuation
        // actually suppresses TaskScheduler.UnobservedTaskException.
        store.EnsureCulture(_german);

        Assert.Null(Resolve(store, _german));
        Assert.Empty(store.LoadedCultures);
    }

    [Fact]
    public void EnsureCulture_FaultingAsyncProvider_DoesNotLeakUnobservedTaskException()
    {
        // The fire-and-forget miss path discards the faulting background load's Task. Without the fault-observing
        // ContinueWith in LoadAndPublish, that Task's exception is never observed and resurfaces from the Task
        // finalizer as TaskScheduler.UnobservedTaskException. Drive the fault, force the discarded Task through
        // finalization, and assert the event never fired for it. Determinism: the source faults *synchronously*
        // (SynchronouslyFaultingAsynchronous — no await before the throw), so the discarded Task is already faulted
        // when EnsureCulture returns; the GC below then observes a settled graph, with no handle on the task and no
        // timing wait. Remove the ContinueWith and this test leaks the exception and fails.
        var leaked = new List<Exception>();
        void Observe(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            // The event is process-global; match only this test's sentinel so no other suite can trip it.
            if (args.Exception.Flatten().InnerExceptions.Any(inner => inner is InvalidOperationException { Message: FaultSentinel }))
            {
                leaked.Add(args.Exception);
            }
        }

        TaskScheduler.UnobservedTaskException += Observe;
        try
        {
            DriveFaultingMiss();

            // The unobserved-exception event is raised from the Task finalizer, so it only fires after a collection
            // drains finalizers (S1215-suppressed for test projects — see .editorconfig).
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Observe;
        }

        Assert.Empty(leaked);

        // A non-inlined local so no stack slot roots the store (or its discarded faulted Task) across the GC above.
        [MethodImpl(MethodImplOptions.NoInlining)]
        void DriveFaultingMiss()
        {
            using CatalogStore store = StoreWith(CultureLoading.OnDemand, new StubProvider(SynchronouslyFaultingAsynchronous("de")));
            store.EnsureCulture(_german);
            Assert.Null(Resolve(store, _german));
        }
    }

    private static CatalogStore StoreWith(CultureLoading loading, params ICatalogProvider[] providers) =>
        new(new LocalizerOptions
        {
            // A directory that does not exist, so the auto-wired directory provider contributes nothing and the
            // test drives the store purely through the stub providers it configures.
            TranslationsDirectory = Path.Combine(Path.GetTempPath(), "apl-empty-" + Guid.NewGuid().ToString("N")),
            SourceCulture = "en",
            CultureLoading = loading,
            Providers = [.. providers.Select(Factory)]
        });

    private static string? Resolve(CatalogStore store, CultureInfo culture) =>
        store.Lookup(culture, Category, "hello");

    private static byte[] ArbBytes(string culture, string message) => Encoding.UTF8.GetBytes($$"""
        {
          "@@locale": "{{culture}}",
          "@@x-category": "{{Category}}",
          "hello": "{{message}}",
          "@hello": { "x-state": "Translated", "x-source-fingerprint": "fp" }
        }
        """);

    private static Catalog ParseArb(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return new ArbTranslationFormat().Read(stream);
    }

    private static Func<LocalizerOptions, ICatalogProvider> Factory(ICatalogProvider provider) => _ => provider;

    private static CatalogSpec Synchronous(string culture, string message) =>
        new(culture, () => new CatalogSource.Synchronous(() => ParseArb(ArbBytes(culture, message))));

    private static CatalogSpec Asynchronous(string culture, string message) =>
        new(culture, () => new CatalogSource.Asynchronous(async _ =>
        {
            // Yield so the load genuinely completes asynchronously, like a real network fetch — the store's
            // synchronous lookup path must skip it rather than blocking.
            await Task.Yield();
            return ParseArb(ArbBytes(culture, message));
        }));

    // An asynchronous load held at a caller-controlled gate, so a test can assert the pre-load state before the
    // background load is allowed to commit. The gate stands in for a slow network fetch, deterministically.
    private static CatalogSpec GatedAsynchronous(string culture, string message, Task gate) =>
        new(culture, () => new CatalogSource.Asynchronous(async _ =>
        {
            await gate.ConfigureAwait(false);
            return ParseArb(ArbBytes(culture, message));
        }));

    // An asynchronous load that faults with a NON-catalog-load-failure exception (InvalidOperationException is not in
    // the loader's caught set), so FetchAsync lets it propagate and the returned task faults — driving the
    // fire-and-forget miss path's graceful degradation.
    private static CatalogSpec FaultingAsynchronous(string culture) =>
        new(culture, () => new CatalogSource.Asynchronous(async _ =>
        {
            await Task.Yield();
            throw new InvalidOperationException("boom");
        }));

    // An asynchronous source that faults SYNCHRONOUSLY — no await before the throw, so OpenAsync returns an
    // already-faulted ValueTask and the store's discarded FetchAsync task is faulted before EnsureCulture returns.
    // That settled state is what lets the unobserved-exception guard test force finalization deterministically.
    private static CatalogSpec SynchronouslyFaultingAsynchronous(string culture) =>
        new(culture, () => new CatalogSource.Asynchronous(
            _ => ValueTask.FromException<Catalog>(new InvalidOperationException(FaultSentinel))));

    private static CatalogSpec Malformed(string culture) =>
        new(culture, () => new CatalogSource.Synchronous(() => ParseArb(Encoding.UTF8.GetBytes("{ not valid arb"))));

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
