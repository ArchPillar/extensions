using System.Collections.Concurrent;
using System.Globalization;
using ArchPillar.Extensions.Localization.Providers;
using ArchPillar.Extensions.Localization.Snapshots;

namespace ArchPillar.Extensions.Localization.Catalogs;

/// <summary>
/// Owns the layered set of translation catalogs and keeps the merged snapshot current. Provider-agnostic: it loads
/// from an ordered list of <see cref="ICatalogProvider"/>s (lowest precedence first) and exposes the merged
/// snapshot for a <see cref="DefaultLocalizer"/>. <c>new CatalogStore(options)</c> is
/// directory-backed; the ambient store (<see cref="Localizer"/>) also discovers embedded and satellite catalogs.
/// </summary>
internal sealed class CatalogStore : IDisposable
{
    #region Fields

    // Guards the configuration/snapshot field swaps (options, providers, loader, snapshot) — all short. The loader's
    // own registry is lock-free, and provider calls (reflection + I/O) are NEVER made under _gate: they take the CLR
    // loader lock, and the resource provider's AssemblyLoad watch fires under that lock, so holding _gate across one
    // would invert lock order and deadlock.
    private readonly object _gate = new();
    private readonly object _startupGate = new();
    // The active configuration, swapped wholesale on construct/Configure; read for the culture-loading mode
    // (eager/on-demand) and the hot-reload setting.
    private volatile LocalizerOptions _options = new();
    // Ambient-store flag: when set, DefaultProviders adds a ResourceCatalogProvider beneath the directory one.
    private readonly bool _discover;
    // The configured providers in precedence order, swapped wholesale on reconfigure.
    private volatile List<ICatalogProvider> _providers = [];
    // The hot-reload watch subscriptions, disposed together on reconfigure and on dispose.
    private readonly List<IDisposable> _watches = [];
    // Loads catalogs and remembers what is loaded (dedup, failures, the in-flight queue); swapped on reconfigure so a
    // new configuration starts with an empty registry. Never publishes — it signals growth and the store rebuilds.
    private volatile CatalogLoader _loader = new([]);
    // In-use cultures, used as a set: the hot-path lookup is lock-free and TryAdd gates each culture's load to one
    // caller, so registering a culture needs no lock or copy.
    private readonly ConcurrentDictionary<string, byte> _loadedCultures = new(StringComparer.OrdinalIgnoreCase);

    private bool _watching;
    private volatile bool _started;
    // Whether a snapshot has been built; a rebuild also builds when this is false, so startup/reconfigure establish
    // the baseline even when nothing loads.
    private bool _snapshotBuilt;
    private volatile TranslationSnapshot _snapshot = TranslationSnapshot.Empty;

    /// <summary>Raised after a rebuild that changed the merged snapshot. Raised once per operation and outside
    /// <c>_gate</c>, so a subscriber may re-enter the store for a lookup. Note that on the startup, reconfigure, and
    /// reset paths it is raised while the outer <c>_startupGate</c> is held: that gate is reentrant, so the raising
    /// thread may itself re-enter the store, but a subscriber must not synchronously block waiting on a <em>different</em>
    /// thread that is calling <see cref="Configure"/>, <see cref="Reset"/>, <see cref="Dispose"/>, or
    /// <see cref="EnsureStarted"/>, or the two threads deadlock on the gate.</summary>
    public event Action? CatalogsChanged;

    #endregion

    #region Properties

    /// <summary>The cultures present in the current merged snapshot — those that contributed a loaded override.</summary>
    public IReadOnlyCollection<string> LoadedCultures
    {
        get
        {
            EnsureStarted();
            return _snapshot.Cultures.Keys;
        }
    }

    #endregion

    #region Queries

    /// <summary>Resolves the loaded override for <paramref name="compositeKey"/> under <paramref name="category"/>,
    /// walking from <paramref name="culture"/> up through its parent cultures, or <see langword="null"/> when none is
    /// loaded. The in-code default is the caller's terminal fallback.</summary>
    public string? Lookup(CultureInfo culture, string category, string compositeKey)
    {
        EnsureStarted();
        return _snapshot.Lookup(culture, category, compositeKey);
    }

    /// <summary>Enumerates the loaded overrides for <paramref name="category"/> in <paramref name="culture"/> as
    /// (composite-key, message) pairs, including parent cultures when requested — a more specific culture wins on
    /// overlap.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> EnumerateCategory(CultureInfo culture, string category, bool includeParentCultures)
    {
        EnsureStarted();
        return _snapshot.EnumerateCategory(culture, category, includeParentCultures);
    }

    #endregion

    #region Construction

    /// <summary>
    /// Initializes a directory-backed <see cref="CatalogStore"/> over <paramref name="options"/>, loading through a
    /// <see cref="DirectoryCatalogProvider"/> immediately and watching it when hot reload is set.
    /// </summary>
    /// <param name="options">The catalogue configuration.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public CatalogStore(LocalizerOptions options)
        : this(options ?? throw new ArgumentNullException(nameof(options)), discover: false)
    {
        EnsureStarted();
    }

    private CatalogStore(LocalizerOptions options, bool discover)
    {
        _discover = discover;
        ApplyOptions(options);
    }

    /// <summary>Creates the process-wide ambient store: auto-wires the resource + directory providers and starts
    /// lazily on first use.</summary>
    public static CatalogStore CreateAmbient() => new(new LocalizerOptions(), discover: true);

    #endregion

    #region Public API

    /// <summary>Re-applies <paramref name="options"/> in one rebuild: rebuilds the provider list from the new options
    /// and swaps in a fresh loader, fully replacing the loaded catalogs. The in-use culture set survives, so the same
    /// cultures reload against the new config.</summary>
    public void Configure(LocalizerOptions options)
    {
        lock (_startupGate)
        {
            ApplyOptions(options);
            EnsureStarted();
        }
    }

    // One-time startup (also forced by LocalizationContext.Load): subscribe watches, then load — eager pulls every
    // provider's whole inventory, on-demand pulls just the in-use cultures. Serialized by _startupGate; provider I/O
    // runs without _gate. Watches are set before the scan so a racing change is not missed; the trailing Rebuild
    // (inside LoadAndPublish) establishes the baseline even when nothing loads. Idempotent.
    public void EnsureStarted()
    {
        if (_started)
        {
            return;
        }

        lock (_startupGate)
        {
            if (_started)
            {
                return;
            }

            IReadOnlyList<ICatalogProvider> providers = _providers;
            if (_options.EnableHotReload && !_watching)
            {
                _watching = true;
                foreach (ICatalogProvider provider in providers)
                {
                    _watches.Add(provider.Watch(descriptor => OnCatalogChanged(provider, descriptor)));
                }
            }

            LoadAndPublish(_options.CultureLoading == CultureLoading.Eager
                ? WorkForInventory(providers)
                : LoadedCultureWork());
            _started = true;

            // On-demand startup reloads just the in-use cultures against the current providers. A reconfigure keeps
            // that set, so this reloads it against the rebuilt providers.
            List<(ICatalogProvider Provider, CatalogDescriptor Descriptor)> LoadedCultureWork()
            {
                var work = new List<(ICatalogProvider Provider, CatalogDescriptor Descriptor)>();
                foreach (var cultureName in _loadedCultures.Keys)
                {
                    AddDescriptors(work, providers, CultureInfo.GetCultureInfo(cultureName));
                }

                return work;
            }
        }
    }

    // Loads the catalogs a culture (and its parents) needs on a lookup miss. Synchronous descriptors load inline and
    // resolve on the next lookup; asynchronous ones go to the loader's background queue (never opened inline — would
    // block in WASM) and surface through CatalogsChanged as they land. The fast path is a lock-free set read.
    public void EnsureCulture(CultureInfo culture)
    {
        EnsureStarted();

        if (_loadedCultures.ContainsKey(culture.Name))
        {
            return;
        }

        MarkChainInUse(culture);
        LoadAndPublish(WorkForChain(culture));
    }

    // Awaited preload of one culture (and its parent chain): loads the same catalogs the miss path does, then drains
    // the background queue so the subsequent lookups resolve with no flash — publishing the whole culture in one
    // rebuild. The token cancels the wait, not the shared fetch.
    public Task LoadCultureAsync(CultureInfo culture, CancellationToken cancellationToken)
    {
        EnsureStarted();

        MarkChainInUse(culture);
        return LoadAndDrainAsync(WorkForChain(culture), cancellationToken);
    }

    // Awaited preload of everything: load each provider's full inventory and drain the queue. CatalogsFor(culture)
    // only ever returns a subset of that inventory, so probing per culture would add nothing to load here.
    public Task PreloadAllAsync(CancellationToken cancellationToken)
    {
        EnsureStarted();

        return LoadAndDrainAsync(WorkForInventory(_providers), cancellationToken);
    }

    /// <summary>Clears the configured providers and all loaded catalogs, returning the store to its default empty state.</summary>
    public void Reset()
    {
        // Return to the default empty state: drop the configured providers (and their loaded catalogs) and the
        // in-use culture set, re-deriving the default context. The ambient store re-discovers its embedded and
        // satellite catalogs on the next use; a directory-backed store falls back to the default directory.
        // Serialized by _startupGate (the outer lock) exactly like Configure, so the watch teardown inside
        // ApplyOptions cannot race a first-time EnsureStarted growing _watches. Rebuild takes _gate internally, so
        // this holds the correct outer→inner order. Rebuild(changed: true) notifies subscribers that the merged
        // snapshot went empty — a populated→empty transition is a real change (an already-empty reset fires one
        // harmless spurious event, far safer than leaving subscribers on a stale snapshot).
        lock (_startupGate)
        {
            ApplyOptions(new LocalizerOptions());
            _loadedCultures.Clear();
            Rebuild(changed: true);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Serialized by _startupGate (the outer lock) so the watch teardown cannot race a first-time EnsureStarted
        // growing _watches — the same discipline Configure and Reset follow.
        lock (_startupGate)
        {
            DisposeWatches();
        }
    }

    #endregion

    #region Internals

    // The single options path, shared by construction and Configure. Rebuilds the provider list (the built-in
    // directory/resource providers plus options.Providers) and swaps in a fresh, empty loader. The in-use culture set
    // survives. Provider construction and old-watch teardown run outside _gate; only the field swap is under it.
    // Resetting _started/_watching/_snapshotBuilt makes the next EnsureStarted re-enumerate and rebuild the baseline.
    private void ApplyOptions(LocalizerOptions options)
    {
        IReadOnlyList<ICatalogProvider> configured = DefaultProviders(options, _discover);
        List<ICatalogProvider> providers = [.. configured, .. options.Providers.Select(factory => factory(options))];

        DisposeWatches();

        lock (_gate)
        {
            _options = options;
            _providers = providers;
            // A fresh loader starts the new configuration with an empty registry; any in-flight load from the old
            // configuration lands into the discarded loader and is ignored.
            _loader = new CatalogLoader(providers);
            _started = false;
            _watching = false;
            _snapshotBuilt = false;
        }
    }

    // The auto-default provider list: a directory provider, with the resource provider beneath it for the ambient
    // store (resource first so app files win on overlap).
    private static IReadOnlyList<ICatalogProvider> DefaultProviders(LocalizerOptions options, bool discover)
    {
        var directoryProvider = new DirectoryCatalogProvider(options);
        return discover ? [new ResourceCatalogProvider(options.Formats), directoryProvider] : [directoryProvider];
    }

    // Records a culture and its parents as in-use, so they are not re-probed on a later miss and reload against the
    // rebuilt providers on a reconfigure. A command, kept separate from building the work (WorkForChain stays a query).
    private void MarkChainInUse(CultureInfo culture)
    {
        foreach (CultureInfo current in CultureChain.Of(culture))
        {
            _loadedCultures.TryAdd(current.Name, 0);
        }
    }

    // The work to load a culture and its parent chain: pairs every provider with the descriptors it lists for each
    // culture in the chain. A pure query — claiming the chain in-use is MarkChainInUse's job.
    private List<(ICatalogProvider Provider, CatalogDescriptor Descriptor)> WorkForChain(CultureInfo culture)
    {
        IReadOnlyList<ICatalogProvider> providers = _providers;
        var work = new List<(ICatalogProvider Provider, CatalogDescriptor Descriptor)>();
        foreach (CultureInfo current in CultureChain.Of(culture))
        {
            AddDescriptors(work, providers, current);
        }

        return work;
    }

    // The work to load every provider's full inventory (eager startup and preload) — CatalogsFor only ever returns a
    // subset of it, so per-culture probing would re-cover what this loads. Cultures outside the allow-list are skipped.
    private List<(ICatalogProvider Provider, CatalogDescriptor Descriptor)> WorkForInventory(IReadOnlyList<ICatalogProvider> providers)
    {
        var work = new List<(ICatalogProvider Provider, CatalogDescriptor Descriptor)>();
        foreach (ICatalogProvider provider in providers)
        {
            foreach (CatalogDescriptor descriptor in provider.Catalogs)
            {
                if (IsCultureLoadable(descriptor.Culture))
                {
                    work.Add((provider, descriptor));
                }
            }
        }

        return work;
    }

    // Pairs every provider with the descriptors it lists for one culture, unless the allow-list excludes it.
    private void AddDescriptors(List<(ICatalogProvider Provider, CatalogDescriptor Descriptor)> work, IReadOnlyList<ICatalogProvider> providers, CultureInfo culture)
    {
        if (!IsCultureLoadable(culture.Name))
        {
            return;
        }

        foreach (ICatalogProvider provider in providers)
        {
            foreach (CatalogDescriptor descriptor in provider.CatalogsFor(culture))
            {
                work.Add((provider, descriptor));
            }
        }
    }

    // Whether catalogs for a culture should load. A null allow-list (options.Cultures) loads every culture; otherwise
    // only the listed cultures load, plus the always-loaded source culture and culture-neutral (empty) base catalogs.
    private bool IsCultureLoadable(string culture)
    {
        IReadOnlyList<string>? allowed = _options.Cultures;
        return allowed is null
            || culture.Length == 0
            || string.Equals(culture, _options.SourceCulture, StringComparison.OrdinalIgnoreCase)
            || allowed.Contains(culture, StringComparer.OrdinalIgnoreCase);
    }

    // Loads the work and publishes: the loader raises onChanged for the synchronous batch and for each asynchronous
    // catalog as it lands (the miss/startup path, no await), so every change publishes on its own. The trailing
    // Rebuild(false) establishes the baseline when nothing loaded — a no-op once a snapshot exists.
    private void LoadAndPublish(IReadOnlyList<(ICatalogProvider Provider, CatalogDescriptor Descriptor)> work)
    {
        // Fire-and-forget: observe each asynchronous load's fault so a non-load-failure exception (e.g. an HttpClient
        // timeout that FetchAsync deliberately lets propagate) is marked observed and cannot resurface later as an
        // UnobservedTaskException. A log provider will surface these; here the store just degrades to the in-code default.
        foreach (Task task in _loader.Load(work, () => Rebuild(changed: true)))
        {
            task.ContinueWith(
                static faulted => _ = faulted.Exception,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }

        Rebuild(changed: false);
    }

    // Loads the work and awaits its asynchronous catalogs, then publishes the whole set in one rebuild (the awaited
    // preload path) — no per-landing flash. onChanged just records that something changed; the trailing rebuild publishes.
    private async Task LoadAndDrainAsync(IReadOnlyList<(ICatalogProvider Provider, CatalogDescriptor Descriptor)> work, CancellationToken cancellationToken)
    {
        var changed = false;
        IReadOnlyList<Task> tasks = _loader.Load(work, () => changed = true);
        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        // Rebuild unconditionally now that the awaited catalogs have landed: a fetch coalesced onto an in-flight
        // task signals through the first caller's onChanged only, so this caller's `changed` can be false even
        // though a catalog landed — forcing the rebuild honors the "snapshot committed before the await returns"
        // guarantee regardless of which caller's callback fired.
        Rebuild(changed, force: true);
    }

    // The single rebuild-and-publish point; takes _gate itself so no caller holds it across the notify. It builds the
    // merged snapshot from the loader's catalogs and swaps it atomically, raising CatalogsChanged outside _gate when
    // <paramref name="changed"/> reflects a real load. A changed:false call still establishes the baseline the first
    // time (startup or reconfigure with nothing loaded), but stays silent. <paramref name="force"/> rebuilds the
    // snapshot even when this caller observed no change (the awaited path, where a coalesced load may have landed).
    private void Rebuild(bool changed, bool force = false)
    {
        lock (_gate)
        {
            if (!changed && !force && _snapshotBuilt)
            {
                return;
            }

            _snapshot = TranslationSnapshot.Build(_loader.LoadedCatalogs(_providers));
            _snapshotBuilt = true;
        }

        if (changed)
        {
            CatalogsChanged?.Invoke();
        }
    }

    // A provider signalled one catalog changed (a hot-reload edit, an assembly load): tell the loader to forget it,
    // then reload it, publishing immediately.
    private void OnCatalogChanged(ICatalogProvider provider, CatalogDescriptor descriptor)
    {
        _loader.Forget(provider, descriptor.Identity);
        LoadAndPublish([(provider, descriptor)]);
    }

    // Stops and drops every hot-reload watch — on reconfigure (the new provider set re-subscribes) and on dispose. An
    // async load already in flight may still land into the current loader; that is harmless.
    private void DisposeWatches()
    {
        foreach (IDisposable watch in _watches)
        {
            watch.Dispose();
        }

        _watches.Clear();
    }

    #endregion
}
