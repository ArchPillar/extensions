using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Xml;
using ArchPillar.Extensions.Localization.Formats;
using ArchPillar.Extensions.Localization.Internal;

namespace ArchPillar.Extensions.Localization;

/// <summary>
/// Owns a layered set of translation catalogs and keeps the merged snapshot current. It is provider-agnostic:
/// it loads from an ordered list of <see cref="ICatalogProvider"/>s (lowest-precedence-first) and never knows
/// where the bytes come from. A plain <c>new CatalogStore(options)</c> is a directory-backed store: it auto-wires
/// a <see cref="DirectoryCatalogProvider"/> over <see cref="LocalizerOptions.TranslationsDirectory"/> and
/// watches it when <see cref="LocalizerOptions.EnableHotReload"/> is set. The process-wide ambient store
/// (<see cref="Localizer"/>) additionally auto-wires a <see cref="ResourceCatalogProvider"/> for the
/// library-embedded and satellite catalogs assemblies ship. A host adds further providers — an HTTP
/// <see cref="ManifestCatalogProvider"/>, say — through <see cref="AddProvider"/>. Either way it exposes the
/// merged snapshot for a <see cref="DefaultLocalizer"/> to resolve against — it is the catalogue source, not a
/// localizer.
/// </summary>
public sealed class CatalogStore : IDisposable
{
    // _gate guards ONLY the in-memory bookkeeping: the per-provider catalog dictionaries, the dedup/failed sets,
    // and the snapshot rebuild/swap — all short, allocation-light operations. The catalog providers (which may do
    // reflection — reading assembly attributes, GetSatelliteAssembly, reading embedded resources — and I/O —
    // reading files or fetching over HTTP) are NEVER invoked while holding _gate. That is deliberate: those
    // operations take the CLR loader lock, and the resource provider's AssemblyLoad watch fires while the loader
    // lock is held. If _gate were held across a loader-lock operation, the watch callback taking _gate would
    // invert the lock order and deadlock. Keeping _gate off the provider calls makes that impossible.
    private readonly object _gate = new();
    private readonly object _startupGate = new();
    private readonly List<Catalog> _hostCatalogs = [];
    private readonly List<ITranslationSource> _sources = [];
    private readonly TranslationFormatRegistry _registry = BuiltInTranslationFormats.CreateRegistry();
    private bool _enableHotReload;
    private IReadOnlyList<string> _formatPrecedence = [];
    private IReadOnlyList<string>? _cultures;
    private bool _eager;
    // Whether this store discovers embedded and satellite catalogs (the ambient store): when set, the
    // auto-default provider list gets a ResourceCatalogProvider beneath the directory provider. Fixed at
    // construction — it is an intrinsic property of the store, not a setting carried on the options.
    private readonly bool _discover;
    // The per-provider state, one entry per registered provider. Replaces the old parallel arrays. The list is
    // swapped wholesale on reconfigure; mutation of a single state's dictionaries is done under _gate.
    private List<ProviderState> _states = [];
    // Providers registered at runtime via AddProvider — kept here so a reconfigure (which rebuilds the
    // options-derived providers) re-appends them rather than dropping them.
    private readonly List<ICatalogProvider> _addedProviders = [];
    // The in-flight asynchronous culture loads, keyed by culture name, so a background load is enqueued once per
    // culture and a concurrent miss coalesces onto the running task rather than firing a second fetch.
    private readonly ConcurrentDictionary<string, Task> _backgroundLoads = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _loadedCultures = new(StringComparer.OrdinalIgnoreCase);
    private RenderingContext _context = RenderingContext.Default;
    private bool _watching;
    private bool _started;
    private TranslationSnapshot _snapshot = TranslationSnapshot.Empty;
    private IReadOnlyList<ITranslationSource> _layers = [];

    /// <summary>Raised after any commit that changed the merged snapshot — a background asynchronous load
    /// landing, a watched catalog reloading. The components layer subscribes to trigger a re-render; an inline
    /// synchronous load resolves directly and needs no event.</summary>
    public event Action? CatalogsChanged;

    /// <summary>The shared rendering context (source culture, missing-argument policy, formatter) — read live
    /// by a localizer over this store, so a configuration change is observed without rebuilding it.</summary>
    internal RenderingContext Context => Volatile.Read(ref _context);

    /// <summary>The source language these catalogs are written in.</summary>
    internal string SourceCultureName => Context.SourceCultureName;

    /// <summary>The current merged snapshot; replaced atomically on every change, so a reader always sees a
    /// consistent view and a resolving localizer observes a change on its next lookup.</summary>
    internal TranslationSnapshot Snapshot
    {
        get
        {
            EnsureStarted();
            return Volatile.Read(ref _snapshot);
        }
    }

    /// <summary>The ordered resolution layers — custom sources (a later-added source wins) above the merged
    /// catalog snapshot — so a localizer resolves every layer the same way, with no special path for sources.</summary>
    internal IReadOnlyList<ITranslationSource> Layers
    {
        get
        {
            EnsureStarted();
            return Volatile.Read(ref _layers);
        }
    }

    // The minimal options BuildSnapshot needs: the source culture (loaded as an override and exempt from the
    // allow-list) and the Cultures allow-list. The store no longer round-trips a full options object.
    private LocalizerOptions SnapshotOptions => new()
    {
        SourceCulture = Context.SourceCultureName,
        Cultures = _cultures
    };

    /// <summary>
    /// Initializes a new directory-backed <see cref="CatalogStore"/> over <paramref name="options"/>. It loads
    /// through a <see cref="DirectoryCatalogProvider"/> over the configured directory immediately, watching it for
    /// changes when <see cref="LocalizerOptions.EnableHotReload"/> is set.
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

    /// <summary>Creates the process-wide ambient store: it auto-wires the resource and directory providers,
    /// discovering embedded and satellite catalogs as assemblies load, and runs its startup lazily on first
    /// use.</summary>
    internal static CatalogStore CreateAmbient() => new(new LocalizerOptions(), discover: true);

    /// <summary>Reloads the catalogs from every provider and swaps the snapshot in atomically.</summary>
    public void Reload()
    {
        EnsureStarted();
        IReadOnlyList<ProviderState> states = Volatile.Read(ref _states);
        var changed = false;
        foreach (ProviderState state in states)
        {
            changed |= ReloadProvider(state);
        }

        if (changed)
        {
            CommitRebuild();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Stop watching (file-system and assembly-load) before returning, so no in-flight change can rebuild a
        // disposed store. Each watch handle owns and disposes its own watcher/timer.
        foreach (ProviderState state in Volatile.Read(ref _states))
        {
            state.Watch?.Dispose();
        }
    }

    /// <summary>Re-applies <paramref name="options"/> in one rebuild — the configuration the ambient store is set
    /// up with (e.g. by AddArchPillarLocalization). It re-derives the rendering context and catalogue settings,
    /// rebuilds the provider list from the options, and reloads, fully replacing the loaded catalogs. Because the
    /// list is rebuilt exactly as at construction, a changed <see cref="LocalizerOptions.TranslationsDirectory"/>
    /// takes effect with no special-casing. Runtime-added providers (<see cref="AddProvider"/>), layered host
    /// catalogs, dynamic sources, and the in-use culture set survive, so the same cultures reload from the new
    /// configuration.</summary>
    internal void Configure(LocalizerOptions options)
    {
        lock (_startupGate)
        {
            ApplyOptions(options);
            EnsureStarted();
        }
    }

    // One-time startup, also run eagerly via LocalizationContext.Load: enumerate every provider's full catalog
    // list when eager, probe the already in-use cultures, and (under hot reload) subscribe each provider's watch.
    // _startupGate (not _gate) serializes this so concurrent first calls run it once; the reflection and I/O
    // inside run WITHOUT _gate, which is taken only for the short commits.
    internal void EnsureStarted()
    {
        if (Volatile.Read(ref _started))
        {
            return;
        }

        lock (_startupGate)
        {
            StartCore();
        }

        // The startup body — a local function so it can only run from inside the _startupGate lock above, never
        // be called without it. Idempotent: returns immediately once started. Sets up the watches before the
        // initial scan so a change racing startup is not missed.
        void StartCore()
        {
            if (Volatile.Read(ref _started))
            {
                return;
            }

            IReadOnlyList<ProviderState> states = Volatile.Read(ref _states);

            if (_enableHotReload && !_watching)
            {
                _watching = true;
                foreach (ProviderState state in states)
                {
                    state.Watch = state.Provider.Watch(descriptor => OnCatalogChanged(state, descriptor));
                }
            }

            // Startup commits the snapshot unconditionally below, so the per-ingest "changed" results are not
            // accumulated here — the helpers run for their side effects (the loaded catalogs), and CatalogsChanged
            // is for later edges, not startup.
            foreach (ProviderState state in states)
            {
                if (_eager)
                {
                    // "Load everything" — every catalog the provider can enumerate, synchronous inline, async queued.
                    Ingest(state, state.Provider.Catalogs);
                }

                // Probe the already in-use cultures (satellites are not enumerable, only probed; on-demand reads
                // its files here too). The per-state dedup keeps an eager catalog from being added twice.
                foreach (var cultureName in Volatile.Read(ref _loadedCultures))
                {
                    IngestCulture(state, CultureInfo.GetCultureInfo(cultureName));
                }
            }

            lock (_gate)
            {
                Rebuild();
            }

            Volatile.Write(ref _started, true);
        }
    }

    // Loads the catalogs a culture (and its parents) needs the first time the culture is used: every provider's
    // CatalogsFor(culture) for the newly in-use cultures — directory files under on-demand, satellite probes for
    // the resource provider. Synchronous descriptors load inline and resolve immediately; asynchronous ones are
    // handed to the background queue (never opened inline — that would block or deadlock in WASM). The fast path
    // is a lock-free set read, so an already-loaded culture pays almost nothing. The switching thread blocks here
    // only for the synchronous loads, so its very next lookup resolves them; the asynchronous ones land later via
    // CatalogsChanged.
    internal void EnsureCulture(CultureInfo culture)
    {
        EnsureStarted();

        if (Volatile.Read(ref _loadedCultures).Contains(culture.Name))
        {
            return;
        }

        // Register the requested culture and its parents as "in use" (cheap, no reflection), collecting the ones
        // this call newly added — the under-lock set Add is the gate, so a culture is loaded exactly once even
        // if two threads request it at the same time.
        var added = new List<string>();
        lock (_gate)
        {
            var loaded = new HashSet<string>(_loadedCultures, StringComparer.OrdinalIgnoreCase);
            for (CultureInfo? current = culture; current is not null && !string.IsNullOrEmpty(current.Name); current = current.Parent)
            {
                if (loaded.Add(current.Name))
                {
                    added.Add(current.Name);
                }
            }

            Volatile.Write(ref _loadedCultures, loaded);
        }

        if (added.Count == 0)
        {
            return;
        }

        // Probe each provider for the newly in-use cultures (provider I/O and reflection run outside _gate); the
        // per-state dedup means an eager directory whose files were already read, or a satellite already probed,
        // is not added twice. Commit and rebuild once so the atomically-swapped snapshot carries the synchronous
        // loads; asynchronous descriptors are queued and surface through CatalogsChanged when they land.
        IReadOnlyList<ProviderState> states = Volatile.Read(ref _states);
        var changed = false;
        foreach (ProviderState state in states)
        {
            foreach (var cultureName in added)
            {
                changed |= IngestCulture(state, CultureInfo.GetCultureInfo(cultureName));
            }
        }

        // The synchronous on-demand path resolves directly on the switching thread's next lookup, so it commits
        // the snapshot but does NOT raise CatalogsChanged (that event is for loads that land later, asynchronously).
        if (changed)
        {
            CommitRebuild();
        }
    }

    // Registers a provider at runtime, appended after the configured providers and kept across reconfiguration.
    // The provider construction and its watch subscription happen on the caller; only the short list growth runs
    // under _gate. Under Eager it ingests the provider's full catalog list now (synchronous inline, asynchronous
    // queued); under OnDemand it ingests nothing up front and EnsureCulture pulls per culture. A synchronous
    // provider's already in-use cultures are probed too, so a provider added after a culture switch still surfaces.
    internal void AddProvider(ICatalogProvider provider)
    {
        var state = new ProviderState(provider);
        if (_enableHotReload && Volatile.Read(ref _watching))
        {
            state.Watch = provider.Watch(descriptor => OnCatalogChanged(state, descriptor));
        }

        lock (_gate)
        {
            _addedProviders.Add(provider);
            _states = [.. _states, state];
        }

        if (!Volatile.Read(ref _started))
        {
            // Not started yet: EnsureStarted will ingest this state along with the rest.
            return;
        }

        var changed = false;
        if (_eager)
        {
            changed |= Ingest(state, provider.Catalogs).SyncChanged;
        }

        foreach (var cultureName in Volatile.Read(ref _loadedCultures))
        {
            changed |= IngestCulture(state, CultureInfo.GetCultureInfo(cultureName));
        }

        // Like EnsureCulture, the synchronous additive path commits but does not raise CatalogsChanged.
        if (changed)
        {
            CommitRebuild();
        }
    }

    // Awaited preload of a single culture: the same ingest the on-demand path uses — synchronous descriptors loaded
    // inline, asynchronous ones enqueued onto the background queue — then awaits those background tasks so the queue
    // drains for this culture (and its parent chain) before returning. There is no second async-open path: preload
    // enqueues exactly like a synchronous miss and just waits for the result, so the subsequent synchronous lookups
    // resolve an already-loaded snapshot with no flash. The cancellation token cancels the wait, not the shared
    // coalesced fetch — a background load joined by several callers cannot be owned by one token. The per-state
    // dedup means a re-select, or an overlap with an on-demand probe, never double-layers.
    internal async Task LoadCultureAsync(CultureInfo culture, CancellationToken cancellationToken)
    {
        EnsureStarted();

        var chain = new List<string>();
        lock (_gate)
        {
            var loaded = new HashSet<string>(_loadedCultures, StringComparer.OrdinalIgnoreCase);
            for (CultureInfo? current = culture; current is not null && !string.IsNullOrEmpty(current.Name); current = current.Parent)
            {
                chain.Add(current.Name);
                loaded.Add(current.Name);
            }

            Volatile.Write(ref _loadedCultures, loaded);
        }

        IReadOnlyList<ProviderState> states = Volatile.Read(ref _states);
        var syncChanged = false;
        var pending = new List<Task>();
        foreach (ProviderState state in states)
        {
            foreach (var cultureName in chain)
            {
                (var changed, IReadOnlyList<Task> tasks) = Ingest(state, state.Provider.CatalogsFor(CultureInfo.GetCultureInfo(cultureName)));
                syncChanged |= changed;
                pending.AddRange(tasks);
            }
        }

        await DrainAsync(syncChanged, pending, cancellationToken).ConfigureAwait(false);
    }

    // Awaited preload of every known culture: the same ingest, run over each provider's enumerable Catalogs plus the
    // in-use culture set, then drained. The eager-server "load everything" for an asynchronous context.
    internal async Task PreloadAllAsync(CancellationToken cancellationToken)
    {
        EnsureStarted();

        IReadOnlyList<ProviderState> states = Volatile.Read(ref _states);
        var cultures = new HashSet<string>(Volatile.Read(ref _loadedCultures), StringComparer.OrdinalIgnoreCase);
        var syncChanged = false;
        var pending = new List<Task>();
        foreach (ProviderState state in states)
        {
            (var changedAll, IReadOnlyList<Task> tasksAll) = Ingest(state, state.Provider.Catalogs);
            syncChanged |= changedAll;
            pending.AddRange(tasksAll);

            foreach (var cultureName in cultures)
            {
                (var changed, IReadOnlyList<Task> tasks) = Ingest(state, state.Provider.CatalogsFor(CultureInfo.GetCultureInfo(cultureName)));
                syncChanged |= changed;
                pending.AddRange(tasks);
            }
        }

        await DrainAsync(syncChanged, pending, cancellationToken).ConfigureAwait(false);
    }

    // The awaited-trigger tail: surface the synchronous loads now (they committed into the provider states during
    // ingest; rebuild the snapshot so they resolve before the await returns), then wait for the enqueued background
    // tasks to drain. Each background task commits and raises CatalogsChanged as it lands, so nothing is committed
    // twice here. The token cancels only this wait.
    private async Task DrainAsync(bool syncChanged, IReadOnlyList<Task> pending, CancellationToken cancellationToken)
    {
        if (syncChanged)
        {
            CommitRebuild();
        }

        if (pending.Count > 0)
        {
            await Task.WhenAll(pending).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Layers a catalog into the store as a host source (a later source wins).</summary>
    internal void AddCatalog(Catalog catalog)
    {
        lock (_gate)
        {
            _hostCatalogs.Add(catalog);
            Rebuild();
        }
    }

    /// <summary>Layers a dynamic source into the store (a later source wins).</summary>
    internal void AddSource(ITranslationSource source)
    {
        lock (_gate)
        {
            // Idempotent for the same source instance: re-registering (e.g. AddArchPillarLocalization called
            // twice with the same options) must not stack duplicate layers onto the process-global store.
            if (_sources.Contains(source))
            {
                return;
            }

            _sources.Add(source);
            Rebuild();
        }
    }

    /// <summary>Clears all layered catalogs, sources, and loaded state, returning the store to empty.</summary>
    internal void Reset()
    {
        lock (_gate)
        {
            foreach (ProviderState state in _states)
            {
                state.Catalogs.Clear();
                state.Failed.Clear();
            }

            _hostCatalogs.Clear();
            _sources.Clear();
            _loadedCultures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Volatile.Write(ref _context, RenderingContext.Default);
            _started = false;
            Rebuild();
        }
    }

    // The single options-application path, shared by construction and Configure so the two can never drift. It
    // re-derives the rendering context, re-reads the catalogue settings, and rebuilds the provider list and its
    // per-provider state from scratch — fully replacing the loaded catalogs. (Constant reconfiguration is not an
    // expected scenario, so a full rebuild is the simple, safe choice.) Runtime additions survive: the host
    // catalogs (AddCatalog), dynamic sources (AddSource), runtime providers (AddProvider), and in-use culture set
    // are untouched, so the following EnsureStarted reloads the same cultures against the new providers. Provider
    // construction and the old watch teardown run OUTSIDE _gate (they take the CLR loader lock); only the short
    // field swaps run under it, so the AssemblyLoad-watch-vs-_gate ordering never inverts. Resetting
    // _started/_watching makes the next EnsureStarted re-enumerate, re-probe the in-use cultures, and re-subscribe.
    private void ApplyOptions(LocalizerOptions options)
    {
        var context = RenderingContext.For(options.SourceCulture, options.MissingArguments);
        IReadOnlyList<ICatalogProvider> configured = DefaultProviders(options.TranslationsDirectory, options.HotReloadDebounce, _discover);
        IReadOnlyList<ICatalogProvider> providers =
            _addedProviders.Count == 0 ? configured : [.. configured, .. _addedProviders];

        foreach (ProviderState state in Volatile.Read(ref _states))
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
            Volatile.Write(ref _context, context);
            _formatPrecedence = options.FormatPrecedence;
            _cultures = options.Cultures;
            _eager = options.CultureLoading == CultureLoading.Eager;
            _enableHotReload = options.EnableHotReload;
            foreach (ITranslationSource source in options.Sources ?? [])
            {
                if (!_sources.Contains(source))
                {
                    _sources.Add(source);
                }
            }

            _states = states;
            _started = false;
            _watching = false;
        }
    }

    // A provider signalled that a single catalog changed (a file edited under hot reload, an assembly loaded):
    // force-reload just that descriptor — clear its identity from the provider's loaded and failed sets, then load
    // per its union (synchronous inline, asynchronous queued) — commit, and raise CatalogsChanged. A load that
    // throws (a deleted file) leaves the identity removed. Runs OUTSIDE _gate, which is exactly why no provider
    // call may sit under _gate: this can fire while the CLR loader lock is held (the resource watch).
    private void OnCatalogChanged(ProviderState state, CatalogDescriptor descriptor)
    {
        (string, string) identity = descriptor.Identity;
        lock (_gate)
        {
            state.Catalogs.Remove(identity);
            state.Failed.Remove(identity);
        }

        // The ingest helpers filter by union arm internally (IngestSynchronous skips non-synchronous descriptors,
        // EnqueueAsynchronous skips non-asynchronous ones), so this needs no explicit switch on descriptor.Source.
        var changed = Ingest(state, [descriptor]).SyncChanged;
        CommitAndNotify(changed);
    }

    // Reloads one provider's full catalog list (used by Reload): clears its committed catalogs and failed marks,
    // then re-ingests the synchronous descriptors inline and re-queues the asynchronous ones. Returns whether the
    // synchronous re-ingest changed anything. The provider call and parse run outside _gate.
    private bool ReloadProvider(ProviderState state)
    {
        IReadOnlyList<CatalogDescriptor> all = state.Provider.Catalogs;
        lock (_gate)
        {
            state.Catalogs.Clear();
            state.Failed.Clear();
        }

        // Eager re-ingests the whole enumerable list (synchronous inline, asynchronous queued); on-demand restricts
        // the enumerable list to the in-use cultures and only ingests the synchronous arm — the asynchronous ones
        // for an in-use culture come through the per-culture probe below.
        var changed = _eager
            ? Ingest(state, all).SyncChanged
            : IngestSynchronous(state, Filtered(all));

        foreach (var cultureName in Volatile.Read(ref _loadedCultures))
        {
            changed |= IngestCulture(state, CultureInfo.GetCultureInfo(cultureName));
        }

        return changed;

        // On-demand reload restricts the enumerable list to the in-use cultures, so a switch's lazily-loaded
        // language survives a reload without pulling in cultures that were never requested.
        IReadOnlyList<CatalogDescriptor> Filtered(IReadOnlyList<CatalogDescriptor> descriptors)
        {
            HashSet<string> loaded = Volatile.Read(ref _loadedCultures);
            return [.. descriptors.Where(descriptor => loaded.Contains(descriptor.Culture))];
        }
    }

    // On-demand probe of one provider for a single culture: ingest its descriptors for that culture, discarding the
    // background tasks (an on-demand trigger does not await them — they land later via CatalogsChanged). Returns
    // whether the synchronous loads changed anything.
    private bool IngestCulture(ProviderState state, CultureInfo culture) =>
        Ingest(state, state.Provider.CatalogsFor(culture)).SyncChanged;

    // The single ingest path every trigger funnels through: load the synchronous descriptors among the set inline
    // (deduped) and enqueue the asynchronous ones onto the background queue. The on-demand triggers — StartCore,
    // EnsureCulture, AddProvider, ReloadProvider, IngestCulture, OnCatalogChanged — take only the SyncChanged flag
    // and let the queued loads land later through CatalogsChanged; the awaited triggers (LoadCultureAsync,
    // PreloadAllAsync) additionally await the returned Pending tasks to drain the queue before returning. There is
    // no separate async-open path: preload enqueues exactly as a synchronous miss does and just waits. The provider
    // open and parse run outside _gate; only the short commit holds it.
    private (bool SyncChanged, IReadOnlyList<Task> Pending) Ingest(ProviderState state, IReadOnlyList<CatalogDescriptor> descriptors)
    {
        var changed = IngestSynchronous(state, descriptors);
        IReadOnlyList<Task> pending = EnqueueAsynchronous(descriptors);
        return (changed, pending);
    }

    // The shared commit-and-raise tail for the paths that surface a later edge (force-reload and the background
    // queue): rebuild the snapshot and raise CatalogsChanged once if anything changed. The synchronous on-demand
    // triggers (EnsureCulture, AddProvider) deliberately do NOT use this — they commit without raising the event
    // because they resolve directly on the caller's next lookup; the awaited triggers commit their synchronous part
    // through DrainAsync and let each background task raise the event as it lands.
    private void CommitAndNotify(bool changed)
    {
        if (changed)
        {
            CommitRebuild();
            CatalogsChanged?.Invoke();
        }
    }

    // Loads the synchronous descriptors among the given set into the provider's state: for each not already loaded
    // or failed, open via the Synchronous union arm and hand the stream to the shared ReadInto. Asynchronous
    // descriptors are skipped here (the background queue handles them). The open and parse run outside _gate; only
    // the short commit holds it. Returns whether anything was added.
    private bool IngestSynchronous(ProviderState state, IReadOnlyList<CatalogDescriptor> descriptors)
    {
        var loaded = new List<(CatalogDescriptor Descriptor, Catalog Catalog)>();
        var failures = new List<(string, string)>();
        foreach (CatalogDescriptor descriptor in descriptors)
        {
            if (descriptor.Source is not CatalogSource.Synchronous synchronous || AlreadyHandled(state, descriptor.Identity))
            {
                continue;
            }

            try
            {
                using Stream stream = synchronous.Open();
                ReadInto(descriptor, stream, loaded, failures);
            }
            catch (Exception exception) when (IsCatalogLoadFailure(exception))
            {
                failures.Add(descriptor.Identity);
            }
        }

        return CommitLoaded(state, loaded, failures);
    }

    // The asynchronous counterpart of IngestSynchronous, the single async-open site — used only by the background
    // loader (RunBackgroundLoadAsync), which both the on-demand miss and the awaited preload feed through. Differs
    // from the synchronous loop in exactly one line — awaiting the open instead of calling it — and shares ReadInto
    // for everything after the stream. Returns whether anything was added.
    private async Task<bool> IngestAsynchronousAsync(ProviderState state, IReadOnlyList<CatalogDescriptor> descriptors, CancellationToken cancellationToken)
    {
        var loaded = new List<(CatalogDescriptor Descriptor, Catalog Catalog)>();
        var failures = new List<(string, string)>();
        foreach (CatalogDescriptor descriptor in descriptors)
        {
            if (descriptor.Source is not CatalogSource.Asynchronous asynchronous || AlreadyHandled(state, descriptor.Identity))
            {
                continue;
            }

            try
            {
                Stream stream = await asynchronous.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using (stream.ConfigureAwait(false))
                {
                    ReadInto(descriptor, stream, loaded, failures);
                }
            }
            catch (Exception exception) when (IsCatalogLoadFailure(exception))
            {
                failures.Add(descriptor.Identity);
            }
        }

        return CommitLoaded(state, loaded, failures);
    }

    // The one place a catalog's bytes become a Catalog: resolve the descriptor's format and parse the already-open
    // stream, staging the parsed catalog into loaded or — when no format matches — the identity into failures. Both
    // ingest loops funnel through this; the only thing they do not share is obtaining the stream (a synchronous
    // source is opened inline, an asynchronous source is awaited), because a synchronous lookup cannot await and an
    // asynchronous fetch cannot block. A parse that throws a known load failure is caught by the caller's try (the
    // same catch that guards the open), so a malformed or missing catalog is marked failed and dropped — never
    // fatal; an unrelated/fatal exception (a bug in a reader, out-of-memory) is left to propagate rather than masked.
    private void ReadInto(CatalogDescriptor descriptor, Stream stream, List<(CatalogDescriptor Descriptor, Catalog Catalog)> loaded, List<(string, string)> failures)
    {
        ITranslationFormat? format = _registry.Resolve(descriptor.Format);
        if (format is null)
        {
            failures.Add(descriptor.Identity);
            return;
        }

        loaded.Add((descriptor, format.Read(stream)));
    }

    // Whether an exception is an expected catalog-load failure — an I/O or parse error the providers and formats
    // actually throw when a catalog is missing or malformed — rather than an unrelated or fatal exception that
    // should propagate. Mirrors the async path's intent (OperationCanceledException is excluded, so a cancellation
    // or async timeout propagates) and narrows the sync path off its former bare catch. The format readers throw
    // JsonException (ARB), XmlException (XLIFF), and NotSupportedException (XLIFF 1.x); FormatException covers the
    // remaining parse shapes; the stream opens throw IOException/UnauthorizedAccessException (file) or
    // HttpRequestException (HTTP manifest).
    private static bool IsCatalogLoadFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or HttpRequestException
            or JsonException
            or XmlException
            or FormatException
            or NotSupportedException;

    // Queues a coalesced background load for every culture an asynchronous descriptor among the given set carries,
    // returning the background tasks so an awaited caller (preload) can drain them; an on-demand caller ignores the
    // result and lets the loads land later via CatalogsChanged. Each queued task awaits the opens, reads
    // synchronously, commits under _gate, raises CatalogsChanged, and removes itself from the in-flight map. On
    // failure the descriptor's identity is marked failed and dropped — no retry, no timer. The synchronous lookup
    // path uses this so an asynchronous catalog never blocks or deadlocks the lookup.
    private IReadOnlyList<Task> EnqueueAsynchronous(IReadOnlyList<CatalogDescriptor> descriptors)
    {
        List<Task>? pending = null;
        foreach (CatalogDescriptor descriptor in descriptors)
        {
            if (descriptor.Source is CatalogSource.Asynchronous)
            {
                // Coalesce by culture: GetOrAdd starts the load once and a concurrent miss joins the running task.
                // The per-descriptor dedup inside the task (AlreadyHandled) means an already-loaded catalog is a
                // no-op even if its culture is re-enqueued. This coalescing-by-culture is safe only because no
                // asynchronous-loading provider emits Watch signals today: a force-reload (OnCatalogChanged) clears
                // the identity then re-enqueues, but if a load for that culture were already in flight, GetOrAdd
                // would hand back the running task — which already listed its descriptors before the clear — and
                // the force-reload would be silently dropped. The only Watch-emitting providers (directory,
                // resource) are synchronous, so this path is unreachable; a future asynchronous-watching provider
                // would need a different force-reload route (e.g. keyed by identity, or a re-enqueue after the
                // in-flight task drains).
                Task task = _backgroundLoads.GetOrAdd(descriptor.Culture, key => RunBackgroundLoadAsync(key));
                (pending ??= []).Add(task);
            }
        }

        return pending ?? [];
    }

    // The body of a coalesced background culture load: opens and reads every provider's asynchronous descriptors
    // for the culture, commits, and raises CatalogsChanged once if anything landed. Always removes the culture
    // from the in-flight map on completion so a later miss can re-enqueue it.
    private async Task RunBackgroundLoadAsync(string culture)
    {
        try
        {
            await Task.Yield();
            var cultureInfo = CultureInfo.GetCultureInfo(culture);
            IReadOnlyList<ProviderState> states = Volatile.Read(ref _states);
            var changed = false;
            // The task is keyed by culture and re-lists every provider's CatalogsFor(culture) itself: the
            // descriptors that EnqueueAsynchronous was called with only gated whether to enqueue this culture, they
            // are not the load list. Re-listing here is harmless because AlreadyHandled dedups whatever a sibling
            // enqueue already loaded.
            foreach (ProviderState state in states)
            {
                IReadOnlyList<CatalogDescriptor> descriptors = state.Provider.CatalogsFor(cultureInfo);
                changed |= await IngestAsynchronousAsync(state, descriptors, CancellationToken.None).ConfigureAwait(false);
            }

            CommitAndNotify(changed);
        }
        finally
        {
            _backgroundLoads.TryRemove(culture, out _);
        }
    }

    // Whether an identity is already committed or marked failed in a provider's state, so it is not re-loaded.
    private bool AlreadyHandled(ProviderState state, (string, string) identity)
    {
        lock (_gate)
        {
            return state.Catalogs.ContainsKey(identity) || state.Failed.Contains(identity);
        }
    }

    // Commits the parsed catalogs and failure marks into the provider's state under _gate, deduping by identity.
    // Returns whether any catalog was added.
    private bool CommitLoaded(ProviderState state, List<(CatalogDescriptor Descriptor, Catalog Catalog)> loaded, List<(string, string)> failures)
    {
        if (loaded.Count == 0 && failures.Count == 0)
        {
            return false;
        }

        var added = false;
        lock (_gate)
        {
            foreach ((CatalogDescriptor descriptor, Catalog catalog) in loaded)
            {
                if (!state.Catalogs.ContainsKey(descriptor.Identity))
                {
                    state.Catalogs[descriptor.Identity] = new LoadedCatalog(catalog, descriptor.Format);
                    added = true;
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

        return added;
    }

    // Rebuilds and swaps the snapshot under _gate (taking the lock for the short commit). Used by the off-gate
    // load paths so the commit is serialized with the others.
    private void CommitRebuild()
    {
        lock (_gate)
        {
            Rebuild();
        }
    }

    // Rebuilds the merged snapshot from the current layers and swaps it in atomically. Callers hold _gate so the
    // dictionaries are consistent; the swap itself is a single volatile write, so lookups never tear. Layered
    // low-to-high precedence by provider order (a later provider wins on overlap); within a provider, catalogs are
    // ordered by format precedence (xliff > arb > po by default) so the later-wins merge resolves overlap the same
    // way regardless of load order. Host catalogs (explicit AddCatalog) layer on top.
    private void Rebuild()
    {
        var all = new List<Catalog>();
        foreach (ProviderState state in _states)
        {
            all.AddRange(OrderedCatalogs(state));
        }

        all.AddRange(_hostCatalogs);
        TranslationSnapshot snapshot = CatalogLoader.BuildSnapshot(all, SnapshotOptions);
        Volatile.Write(ref _snapshot, snapshot);

        // The resolution layers: custom sources first (a later-added source wins, so iterate newest-first),
        // then the snapshot as the lowest layer. A localizer walks these in order, treating every layer alike.
        var layers = new List<ITranslationSource>(_sources.Count + 1);
        for (var index = _sources.Count - 1; index >= 0; index--)
        {
            layers.Add(_sources[index]);
        }

        layers.Add(new SnapshotTranslationSource(snapshot));
        Volatile.Write(ref _layers, layers);
    }

    // One provider's catalogs, lowest-precedence format first then tie-broken by the catalog's order key, so the
    // layered last-wins merge reproduces the format precedence deterministically rather than depending on the
    // dictionary's enumeration order.
    private List<Catalog> OrderedCatalogs(ProviderState state)
    {
        var entries = new List<KeyValuePair<(string Culture, string Name), LoadedCatalog>>(state.Catalogs);

        // Lowest-precedence format first, then — for equal-rank catalogs (same format, e.g. two .arb files for one
        // culture) — by ordinal name, so the later-named file lands last and wins the layered merge. Without the
        // tie-break, equal-rank order would be the dictionary's enumeration order (effectively the file system's),
        // which differs across machines and makes the winner non-deterministic.
        entries.Sort((left, right) =>
        {
            var byRank = Rank(right.Value.Format).CompareTo(Rank(left.Value.Format));
            return byRank != 0 ? byRank : string.CompareOrdinal(left.Key.Name, right.Key.Name);
        });

        var catalogs = new List<Catalog>(entries.Count);
        foreach (KeyValuePair<(string Culture, string Name), LoadedCatalog> entry in entries)
        {
            catalogs.Add(entry.Value.Catalog);
        }

        return catalogs;
    }

    // The auto-default provider list for a directory: a directory provider, with the resource provider beneath
    // it for the ambient/discover store (resource first so app files win on overlap).
    private static IReadOnlyList<ICatalogProvider> DefaultProviders(string directory, TimeSpan debounce, bool discover)
    {
        var directoryProvider = new DirectoryCatalogProvider(directory, debounce);
        return discover ? [new ResourceCatalogProvider(), directoryProvider] : [directoryProvider];
    }

    // The precedence rank of a format, lower wins on the last-wins merge once OrderedCatalogs places the higher
    // rank later. An unranked or unresolved format sorts to the bottom (int.MaxValue).
    private int Rank(string format)
    {
        ITranslationFormat? resolved = _registry.Resolve(format);
        if (resolved is null)
        {
            return int.MaxValue;
        }

        for (var index = 0; index < _formatPrecedence.Count; index++)
        {
            if (string.Equals(_formatPrecedence[index], resolved.FormatId, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    // A committed catalog and the format it was parsed from, kept so Rebuild can order by format precedence.
    private sealed record LoadedCatalog(Catalog Catalog, string Format);

    // One provider's mutable bookkeeping: the committed catalogs (keyed by identity, deduped), the identities
    // that failed to load (dropped, not retried), and the watch handle. Mutated only under the store's _gate.
    private sealed class ProviderState(ICatalogProvider provider)
    {
        public ICatalogProvider Provider { get; } = provider;

        public Dictionary<(string Culture, string Name), LoadedCatalog> Catalogs { get; } = [];

        public HashSet<(string Culture, string Name)> Failed { get; } = [];

        public IDisposable? Watch { get; set; }
    }
}
