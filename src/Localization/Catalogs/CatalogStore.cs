using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Xml;
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

    // Guards only the in-memory bookkeeping (provider dictionaries, dedup/failed sets, snapshot swap) — all short.
    // Provider calls (reflection + I/O) are NEVER made under _gate: they take the CLR loader lock, and the resource
    // provider's AssemblyLoad watch fires under that lock, so holding _gate across one would invert lock order and
    // deadlock.
    private readonly object _gate = new();
    private readonly object _startupGate = new();
    // The active configuration, swapped wholesale on construct/Configure; read live for the Cultures allow-list,
    // eager/on-demand, and hot reload.
    private volatile LocalizerOptions _options = new();
    // Ambient-store flag: when set, DefaultProviders adds a ResourceCatalogProvider beneath the directory one.
    private readonly bool _discover;
    // One entry per provider, swapped wholesale on reconfigure; a state's dictionaries mutate only under _gate.
    private volatile List<ProviderState> _states = [];
    // In-flight async loads, keyed by descriptor identity so a load is enqueued once and concurrent misses coalesce.
    private readonly ConcurrentDictionary<(string Culture, string Name), Task> _backgroundLoads = new();
    // In-use cultures, used as a set: the hot-path lookup is lock-free and TryAdd gates each culture's load to one
    // caller, so registering a culture needs no lock or copy.
    private readonly ConcurrentDictionary<string, byte> _loadedCultures = new(StringComparer.OrdinalIgnoreCase);

    private volatile RenderingContext _context = RenderingContext.Default;
    private bool _watching;
    private volatile bool _started;
    // Whether a snapshot has been built; a rebuild also fires when this is false, so startup/reconfigure establish
    // the baseline even when nothing loads.
    private bool _snapshotBuilt;
    // Set under _gate when a catalog is committed; cleared by the publishing rebuild.
    private bool _dirty;
    // Open-batch count; while positive, rebuilds defer. Read/written only under _gate.
    private int _batchDepth;
    private volatile TranslationSnapshot _snapshot = TranslationSnapshot.Empty;

    /// <summary>Raised after a rebuild that changed the merged snapshot. Raised once per operation and outside
    /// <c>_gate</c>, so a subscriber may re-enter the store.</summary>
    public event Action? CatalogsChanged;

    #endregion

    #region Properties

    /// <summary>The shared rendering context, read live so a configuration change is seen without rebuilding the
    /// localizer.</summary>
    public RenderingContext Context => _context;

    /// <summary>The source language these catalogs are written in.</summary>
    public string SourceCultureName => Context.SourceCultureName;

    /// <summary>The current merged snapshot, swapped atomically on every change.</summary>
    public TranslationSnapshot Snapshot
    {
        get
        {
            EnsureStarted();
            return _snapshot;
        }
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

    /// <summary>Re-applies <paramref name="options"/> in one rebuild: re-derives the context and rebuilds the
    /// provider list and layered sources from the new options, fully replacing the loaded catalogs. The in-use
    /// culture set survives, so the same cultures reload against the new config.</summary>
    public void Configure(LocalizerOptions options)
    {
        lock (_startupGate)
        {
            ApplyOptions(options);
            EnsureStarted();
        }
    }

    // One-time startup (also forced by LocalizationContext.Load): subscribe watches, then either eager-load every
    // provider whole or (on-demand) probe just the in-use cultures. Serialized by _startupGate; provider I/O runs
    // without _gate. Watches are set before the scan so a racing change is not missed; the batch rebuilds even when
    // nothing loads, establishing the baseline. Idempotent.
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

            IReadOnlyList<ProviderState> states = _states;

            if (_options.EnableHotReload && !_watching)
            {
                _watching = true;
                foreach (ProviderState state in states)
                {
                    state.Watch = state.Provider.Watch(descriptor => OnCatalogChanged(state, descriptor));
                }
            }

            var eager = _options.CultureLoading == CultureLoading.Eager;
            BeginBatch();
            try
            {
                foreach (ProviderState state in states)
                {
                    if (eager)
                    {
                        // The provider's full inventory is the complete set, so load it whole; CatalogsFor only ever
                        // returns a subset of it, so per-culture probing would re-cover what this loads.
                        Load(state, state.Provider.Catalogs);
                    }
                    else
                    {
                        // On-demand: pull only the in-use cultures. A reconfigure keeps that set, so this reloads them
                        // against the rebuilt providers (else their retained in-use mark makes EnsureCulture skip it).
                        foreach (var cultureName in _loadedCultures.Keys)
                        {
                            LoadCulture(state, CultureInfo.GetCultureInfo(cultureName));
                        }
                    }
                }
            }
            finally
            {
                EndBatch();
            }

            _started = true;
        }
    }

    // Loads the catalogs a culture (and its parents) needs on a lookup miss. Synchronous descriptors load inline and
    // resolve on the next lookup; asynchronous ones go to the background queue (never opened inline — would block in
    // WASM) and surface through CatalogsChanged as they land. The fast path is a lock-free set read.
    public void EnsureCulture(CultureInfo culture)
    {
        EnsureStarted();

        if (_loadedCultures.ContainsKey(culture.Name))
        {
            return;
        }

        BeginBatch();
        try
        {
            LoadChain(culture, pending: null);
        }
        finally
        {
            EndBatch();
        }
    }

    // Awaited preload of one culture (and its parent chain): loads the same catalogs the miss path does, then drains
    // the background queue so the subsequent lookups resolve with no flash. The drain sits inside the batch, so the
    // whole culture publishes in one rebuild when it closes. The token cancels the wait, not the shared fetch.
    public async Task LoadCultureAsync(CultureInfo culture, CancellationToken cancellationToken)
    {
        EnsureStarted();

        var pending = new List<Task>();
        BeginBatch();
        try
        {
            LoadChain(culture, pending);
            await DrainAsync(pending, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            EndBatch();
        }
    }

    // Awaited preload of everything: load each provider's full inventory and drain the queue. CatalogsFor(culture)
    // only ever returns a subset of that inventory, so probing per culture would add nothing to load here.
    public async Task PreloadAllAsync(CancellationToken cancellationToken)
    {
        EnsureStarted();

        IReadOnlyList<ProviderState> states = _states;
        var pending = new List<Task>();
        BeginBatch();
        try
        {
            foreach (ProviderState state in states)
            {
                Load(state, state.Provider.Catalogs, pending);
            }

            await DrainAsync(pending, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            EndBatch();
        }
    }

    /// <summary>Clears the configured providers and all loaded catalogs, returning the store to its default empty state.</summary>
    public void Reset()
    {
        // Return to the default empty state: drop the configured providers (and their loaded catalogs) and the
        // in-use culture set, re-deriving the default context. The ambient store re-discovers its embedded and
        // satellite catalogs on the next use; a directory-backed store falls back to the default directory.
        ApplyOptions(new LocalizerOptions());
        _loadedCultures.Clear();
        Rebuild();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Stop the watches so no further change is signalled; an async load already in flight may still land. Each
        // handle owns its watcher.
        foreach (ProviderState state in _states)
        {
            state.Watch?.Dispose();
        }
    }

    #endregion

    #region Internals

    // The single options path, shared by construction and Configure. Re-derives the context and rebuilds the
    // provider list (the built-in directory/resource providers plus options.Providers) and per-provider state from
    // scratch. The in-use culture set survives.
    // Provider construction and old-watch teardown run outside _gate (loader lock); only the field swap is under it.
    // Resetting _started/_watching/_snapshotBuilt makes the next EnsureStarted re-enumerate and rebuild the baseline.
    private void ApplyOptions(LocalizerOptions options)
    {
        var context = RenderingContext.For(options.SourceCulture, options.MissingArguments);
        IReadOnlyList<ICatalogProvider> configured = DefaultProviders(options, _discover);
        IReadOnlyList<ICatalogProvider> providers = [.. configured, .. options.Providers.Select(factory => factory(options))];

        foreach (ProviderState state in _states)
        {
            state.Watch?.Dispose();
        }

        var states = new List<ProviderState>(providers.Count);
        foreach (ICatalogProvider provider in providers)
        {
            states.Add(new ProviderState(provider));
        }

        lock (_gate)
        {
            _options = options;
            _context = context;

            _states = states;
            _started = false;
            _watching = false;
            _dirty = false;
            _snapshotBuilt = false;
        }
    }

    // The auto-default provider list: a directory provider, with the resource provider beneath it for the ambient
    // store (resource first so app files win on overlap).
    private static IReadOnlyList<ICatalogProvider> DefaultProviders(LocalizerOptions options, bool discover)
    {
        var directoryProvider = new DirectoryCatalogProvider(options.TranslationsDirectory, options.HotReloadDebounce, options.Formats);
        return discover ? [new ResourceCatalogProvider(options.Formats), directoryProvider] : [directoryProvider];
    }

    // Marks the culture and its parent chain in-use (TryAdd gates each load to one caller) and probes every provider
    // for each. An awaited caller passes a list to collect the background tasks; the miss path passes null and lets
    // the asynchronous loads publish individually as they land.
    private void LoadChain(CultureInfo culture, List<Task>? pending)
    {
        IReadOnlyList<ProviderState> states = _states;
        for (CultureInfo? current = culture; current is not null && !string.IsNullOrEmpty(current.Name); current = current.Parent)
        {
            _loadedCultures.TryAdd(current.Name, 0);
            foreach (ProviderState state in states)
            {
                LoadCulture(state, current, pending);
            }
        }
    }

    // Probes one provider for a culture. An awaited caller passes a list to collect the background tasks.
    private void LoadCulture(ProviderState state, CultureInfo culture, List<Task>? pending = null) =>
        Load(state, state.Provider.CatalogsFor(culture), pending);

    // The single load path: per descriptor, a synchronous source is opened and parsed inline; an asynchronous one
    // is enqueued onto the coalesced background queue (its task collected when an awaited caller passed a list).
    // Already loaded/failed catalogs are skipped. Provider open and parse run outside _gate.
    private void Load(ProviderState state, IReadOnlyList<CatalogDescriptor> descriptors, List<Task>? pending = null)
    {
        var loaded = new List<(CatalogDescriptor Descriptor, Catalog Catalog)>();
        var failures = new List<(string, string)>();
        foreach (CatalogDescriptor descriptor in descriptors)
        {
            if (AlreadyHandled(state, descriptor.Identity))
            {
                continue;
            }

            switch (descriptor.Source)
            {
                case CatalogSource.Synchronous synchronous:
                    try
                    {
                        loaded.Add((descriptor, synchronous.Open()));
                    }
                    catch (Exception exception) when (IsCatalogLoadFailure(exception))
                    {
                        failures.Add(descriptor.Identity);
                    }

                    break;

                case CatalogSource.Asynchronous:
                    // Always enqueue first, then collect — a null-conditional add on the result would skip the
                    // enqueue itself when no list was passed.
                    Task task = EnqueueAsync(state, descriptor);
                    pending?.Add(task);
                    break;
            }
        }

        CommitLoaded(state, loaded, failures);
    }

    // Whether an identity is already committed or marked failed, so it is not re-loaded.
    private bool AlreadyHandled(ProviderState state, (string, string) identity)
    {
        lock (_gate)
        {
            return state.Catalogs.ContainsKey(identity) || state.Failed.Contains(identity);
        }
    }

    // Coalesces a background load by identity: GetOrAdd starts it once; concurrent misses join the running task.
    private Task EnqueueAsync(ProviderState state, CatalogDescriptor descriptor) =>
        _backgroundLoads.GetOrAdd(descriptor.Identity, _ => RunBackgroundLoadAsync(state, descriptor));

    // Background body for one async catalog: open, parse, commit (publishes when it lands), and remove from the
    // in-flight map. The only async-open site, so the miss path never opens a stream inline.
    private async Task RunBackgroundLoadAsync(ProviderState state, CatalogDescriptor descriptor)
    {
        try
        {
            if (descriptor.Source is not CatalogSource.Asynchronous asynchronous || AlreadyHandled(state, descriptor.Identity))
            {
                return;
            }

            var loaded = new List<(CatalogDescriptor Descriptor, Catalog Catalog)>();
            var failures = new List<(string, string)>();
            try
            {
                Catalog catalog = await asynchronous.OpenAsync(CancellationToken.None).ConfigureAwait(false);
                loaded.Add((descriptor, catalog));
            }
            catch (Exception exception) when (IsCatalogLoadFailure(exception))
            {
                failures.Add(descriptor.Identity);
            }

            CommitLoaded(state, loaded, failures);
        }
        finally
        {
            _backgroundLoads.TryRemove(descriptor.Identity, out _);
        }
    }

    // Awaits the enqueued background tasks; each publishes as it lands. The token cancels only the wait.
    private static async Task DrainAsync(IReadOnlyList<Task> pending, CancellationToken cancellationToken)
    {
        if (pending.Count > 0)
        {
            await Task.WhenAll(pending).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    // Opens a batch; rebuilds defer until the matching EndBatch. Nestable. Pair with EndBatch in a finally.
    private void BeginBatch()
    {
        lock (_gate)
        {
            _batchDepth++;
        }
    }

    // Closes a batch, then rebuilds — publishes now if this was the outermost batch, else defers again.
    private void EndBatch()
    {
        lock (_gate)
        {
            _batchDepth--;
        }

        Rebuild();
    }

    // Commits the parsed catalogs and failures under _gate, deduping by identity and setting the dirty flag when a
    // catalog is actually added. The trailing Rebuild then publishes the change or, inside a batch, defers it.
    private void CommitLoaded(ProviderState state, List<(CatalogDescriptor Descriptor, Catalog Catalog)> loaded, List<(string, string)> failures)
    {
        if (loaded.Count == 0 && failures.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            foreach ((CatalogDescriptor descriptor, Catalog catalog) in loaded)
            {
                if (!state.Catalogs.ContainsKey(descriptor.Identity))
                {
                    state.Catalogs[descriptor.Identity] = catalog;
                    _dirty = true;
                }
            }

            foreach ((string, string) identity in failures)
            {
                if (!state.Catalogs.ContainsKey(identity))
                {
                    state.Failed.Add(identity);
                }
            }
        }

        Rebuild();
    }

    // The single rebuild-and-publish point; takes _gate itself so no caller holds it across the notify. In a batch it
    // defers (marks dirty). Otherwise it asks the snapshot builder for the merged snapshot, swaps it atomically,
    // and raises CatalogsChanged outside _gate — but only for a real change; a baseline-only build is
    // silent.
    private void Rebuild()
    {
        var publish = false;
        lock (_gate)
        {
            if (_batchDepth > 0)
            {
                _dirty = true;
                return;
            }

            if (!_dirty && _snapshotBuilt)
            {
                return;
            }

            publish = _dirty;
            _dirty = false;

            _snapshot = TranslationSnapshot.Build(_states);
            _snapshotBuilt = true;
        }

        if (publish)
        {
            CatalogsChanged?.Invoke();
        }
    }

    // A provider signalled one catalog changed (a hot-reload edit, an assembly load): clear its identity and reload
    // it. Not batched, so a synchronous reload publishes immediately. Runs outside _gate (may fire under the loader lock).
    private void OnCatalogChanged(ProviderState state, CatalogDescriptor descriptor)
    {
        (string, string) identity = descriptor.Identity;
        lock (_gate)
        {
            state.Catalogs.Remove(identity);
            state.Failed.Remove(identity);
        }

        Load(state, [descriptor]);
    }

    // Whether an exception is an expected catalog-load failure (missing/malformed) rather than an unrelated or fatal
    // one that should propagate. OperationCanceledException is excluded, so cancellation propagates.
    private static bool IsCatalogLoadFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or HttpRequestException
            or JsonException
            or XmlException
            or FormatException
            or NotSupportedException;

    #endregion
}
