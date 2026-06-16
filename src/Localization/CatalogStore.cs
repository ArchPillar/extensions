using System.Globalization;
using System.Reflection;
using ArchPillar.Extensions.Localization.Formats;
using ArchPillar.Extensions.Localization.Internal;

namespace ArchPillar.Extensions.Localization;

/// <summary>
/// Owns a layered set of translation catalogs and keeps the merged snapshot current. A plain
/// <c>new CatalogStore(options)</c> is a directory-backed store: it loads the configured
/// <see cref="LocalizerOptions.TranslationsDirectory"/> and watches it when
/// <see cref="LocalizerOptions.EnableHotReload"/> is set. The process-wide ambient store
/// (<see cref="Localizer"/>) additionally discovers library-embedded and satellite catalogs as assemblies
/// load. Either way it exposes the merged snapshot for a <see cref="DefaultLocalizer"/> to resolve against —
/// it is the catalogue source, not a localizer.
/// </summary>
public sealed class CatalogStore : IDisposable
{
    // _gate guards ONLY the in-memory bookkeeping: the catalog lists, the discovery sets, and the snapshot
    // rebuild/swap — all short, allocation-light operations. Reflection and I/O (reading assembly attributes,
    // GetSatelliteAssembly, reading embedded resources, reading files) are NEVER done while holding _gate.
    // That is deliberate: those operations take the CLR loader lock, and the AssemblyLoad handler runs while
    // the loader lock is held. If _gate were held across a loader-lock operation, the handler taking _gate
    // would invert the lock order and deadlock. Keeping _gate off the reflection paths makes that impossible.
    private readonly object _gate = new();
    private readonly object _startupGate = new();
    private readonly bool _discover;
    private readonly List<Catalog> _embeddedCatalogs = [];
    private readonly List<Catalog> _satelliteCatalogs = [];
    private readonly List<Catalog> _directoryCatalogs = [];
    private readonly List<Catalog> _hostCatalogs = [];
    private readonly List<ITranslationSource> _sources = [];
    private readonly List<Assembly> _satelliteAssemblies = [];
    private readonly HashSet<Assembly> _seen = [];
    private readonly HashSet<(Assembly Assembly, string Culture)> _satellitePairs = [];
    private readonly TranslationFormatRegistry _registry = BuildRegistry();
    private readonly bool _enableHotReload;
    private readonly TimeSpan _hotReloadDebounce;
    private readonly IReadOnlyList<string> _formatPrecedence;
    private readonly IReadOnlyList<string>? _cultures;
    private readonly bool _loadOnDemand;
    private HashSet<string> _loadedCultures = new(StringComparer.OrdinalIgnoreCase);
    private RenderingContext _context;
    private string _directory;
    private bool _subscribed;
    private bool _scannedLoaded;
    private volatile bool _hasSatellites;
    private TranslationSnapshot _snapshot = TranslationSnapshot.Empty;
    private IReadOnlyList<ITranslationSource> _layers = [];
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;

    /// <summary>
    /// Initializes a new directory-backed <see cref="CatalogStore"/> over <paramref name="options"/>, loading
    /// the configured directory immediately and watching it for changes when
    /// <see cref="LocalizerOptions.EnableHotReload"/> is set.
    /// </summary>
    /// <param name="options">The catalogue configuration.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public CatalogStore(LocalizerOptions options)
        : this(options ?? throw new ArgumentNullException(nameof(options)), discover: false)
    {
        // On-demand defers each culture's files to the first lookup that requests it (EnsureCulture); eager
        // reads the whole directory now.
        if (!_loadOnDemand)
        {
            _directoryCatalogs.AddRange(CatalogLoader.LoadDirectory(Options));
        }

        Rebuild();
        _scannedLoaded = true;
        if (_enableHotReload)
        {
            StartWatching();
        }
    }

    private CatalogStore(LocalizerOptions options, bool discover)
    {
        _discover = discover;
        _context = RenderingContext.For(options.SourceCulture, options.MissingArguments);
        _formatPrecedence = options.FormatPrecedence;
        _cultures = options.Cultures;
        _loadOnDemand = options.CultureLoading == CultureLoading.OnDemand;
        _directory = options.TranslationsDirectory;
        _enableHotReload = options.EnableHotReload;
        _hotReloadDebounce = options.HotReloadDebounce;
        _sources.AddRange(options.Sources ?? []);
    }

    /// <summary>Creates the process-wide ambient store: it discovers embedded and satellite catalogs as
    /// assemblies load and reads its directory lazily on first use.</summary>
    internal static CatalogStore CreateAmbient() => new(new LocalizerOptions(), discover: true);

    /// <summary>The shared rendering context (source culture, missing-argument policy, formatter) — read live
    /// by a localizer over this store, so a configuration change is observed without rebuilding it.</summary>
    internal RenderingContext Context => Volatile.Read(ref _context);

    /// <summary>The configuration this store currently reflects, used when merging the catalog layers.</summary>
    internal LocalizerOptions Options => new()
    {
        SourceCulture = Context.SourceCultureName,
        MissingArguments = Context.MissingArguments,
        Sources = [.. _sources],
        FormatPrecedence = _formatPrecedence,
        Cultures = _cultures,
        CultureLoading = _loadOnDemand ? CultureLoading.OnDemand : CultureLoading.Eager,
        TranslationsDirectory = _directory,
        EnableHotReload = _enableHotReload,
        HotReloadDebounce = _hotReloadDebounce
    };

    /// <summary>The current merged snapshot; replaced atomically on every change, so a reader always sees a
    /// consistent view and a resolving localizer observes a change on its next lookup.</summary>
    internal TranslationSnapshot Snapshot
    {
        get
        {
            if (_discover)
            {
                EnsureStarted();
            }

            return Volatile.Read(ref _snapshot);
        }
    }

    /// <summary>The ordered resolution layers — custom sources (a later-added source wins) above the merged
    /// catalog snapshot — so a localizer resolves every layer the same way, with no special path for sources.</summary>
    internal IReadOnlyList<ITranslationSource> Layers
    {
        get
        {
            if (_discover)
            {
                EnsureStarted();
            }

            return Volatile.Read(ref _layers);
        }
    }

    // Applies source culture, missing-argument policy, the directory, and additional sources in one rebuild —
    // the single-shot configuration the ambient store is set up with (e.g. by AddArchPillarLocalization).
    internal void Configure(LocalizerOptions options)
    {
        var context = RenderingContext.For(options.SourceCulture, options.MissingArguments);
        var directory = string.IsNullOrEmpty(options.TranslationsDirectory) ? null : options.TranslationsDirectory;

        lock (_startupGate)
        {
            lock (_gate)
            {
                Volatile.Write(ref _context, context);
                if (directory is not null)
                {
                    _directory = directory;
                }

                foreach (ITranslationSource source in options.Sources ?? [])
                {
                    if (!_sources.Contains(source))
                    {
                        _sources.Add(source);
                    }
                }
            }

            // Already started: re-read the directory layer against the new directory; otherwise EnsureStarted
            // reads it on first use. I/O (the file read) stays off _gate. On-demand re-reads only the cultures
            // already in use, so a switch's lazily-loaded language survives a reconfigure.
            if (Volatile.Read(ref _scannedLoaded))
            {
                List<Catalog> catalogs = CatalogLoader.LoadDirectory(Options, DirectoryCultureFilter());
                lock (_gate)
                {
                    _directoryCatalogs.Clear();
                    _directoryCatalogs.AddRange(catalogs);
                    Rebuild();
                }
            }
            else
            {
                lock (_gate)
                {
                    Rebuild();
                }
            }
        }
    }

    /// <summary>Reloads the catalogs from the translations directory and swaps the snapshot in atomically.</summary>
    public void Reload()
    {
        List<Catalog> catalogs = CatalogLoader.LoadDirectory(Options, DirectoryCultureFilter());
        lock (_gate)
        {
            _directoryCatalogs.Clear();
            _directoryCatalogs.AddRange(catalogs);
            Rebuild();
        }
    }

    // In on-demand mode, restrict a directory (re)load to the cultures already in use; eager loads them all.
    // The in-use set is replaced wholesale, never mutated, so the read reference is a stable snapshot.
    private IReadOnlySet<string>? DirectoryCultureFilter() =>
        _loadOnDemand ? Volatile.Read(ref _loadedCultures) : null;

    /// <summary>Eagerly loads the catalogs now — for the ambient store, runs its lazy startup (directory read
    /// + assembly discovery) up front instead of on first lookup. A no-op for an already-loaded store.</summary>
    internal void EnsureLoaded()
    {
        if (_discover)
        {
            EnsureStarted();
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

    /// <summary>Clears all layered catalogs, sources, and discovery state, returning the store to empty.</summary>
    internal void Reset()
    {
        lock (_gate)
        {
            _embeddedCatalogs.Clear();
            _satelliteCatalogs.Clear();
            _directoryCatalogs.Clear();
            _hostCatalogs.Clear();
            _sources.Clear();
            _satelliteAssemblies.Clear();
            _seen.Clear();
            _satellitePairs.Clear();
            _loadedCultures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Volatile.Write(ref _context, RenderingContext.Default);
            _directory = DefaultDirectory();
            _scannedLoaded = false;
            _hasSatellites = false;
            Rebuild();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Stop and unsubscribe the watcher before disposing the timer, so no in-flight change event can call
        // Change(...) on a disposed timer.
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnChanged;
            _watcher.Created -= OnChanged;
            _watcher.Deleted -= OnChanged;
            _watcher.Dispose();
        }

        _debounce?.Dispose();
    }

    // Loads the catalogs a culture (and its parents) needs the first time the culture is used: its satellites
    // always, and — under on-demand loading — its directory files too. The fast path is a volatile bool plus a
    // lock-free set read, so an already-loaded culture (and an eager files-only app) pays almost nothing. The
    // switching thread blocks here until the load is committed, so its very next lookup resolves the new
    // culture; no restart, and a culture loaded once is never re-read.
    internal void EnsureCulture(CultureInfo culture)
    {
        if (_discover)
        {
            EnsureStarted();
        }

        if (!_hasSatellites && !_loadOnDemand)
        {
            return;
        }

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

        // On-demand: read the newly in-use cultures' files outside the lock, then commit and rebuild so the
        // atomically-swapped snapshot carries them. Eager has already read every culture, so this is skipped.
        if (_loadOnDemand && added.Count > 0)
        {
            List<Catalog> catalogs = CatalogLoader.LoadDirectory(Options, new HashSet<string>(added, StringComparer.OrdinalIgnoreCase));
            lock (_gate)
            {
                _directoryCatalogs.AddRange(catalogs);
                Rebuild();
            }
        }

        ReconcileSatellites();
    }

    // One-time startup: subscribe to assembly loads, read the directory layer, and discover every
    // already-loaded assembly. _startupGate (not _gate) serializes this so concurrent first calls run it
    // once; the reflection and I/O inside run WITHOUT _gate, which is taken only for the short commits.
    private void EnsureStarted()
    {
        if (Volatile.Read(ref _scannedLoaded))
        {
            return;
        }

        lock (_startupGate)
        {
            if (Volatile.Read(ref _scannedLoaded))
            {
                return;
            }

            lock (_gate)
            {
                if (!_subscribed)
                {
                    _subscribed = true;
                    AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
                }
            }

            if (!_loadOnDemand)
            {
                List<Catalog> directory = CatalogLoader.LoadDirectory(Options);
                lock (_gate)
                {
                    _directoryCatalogs.Clear();
                    _directoryCatalogs.AddRange(directory);
                }
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                DiscoverAssembly(assembly, rebuild: false);
            }

            lock (_gate)
            {
                Rebuild();
            }

            Volatile.Write(ref _scannedLoaded, true);
        }
    }

    private void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args) =>
        DiscoverAssembly(args.LoadedAssembly, rebuild: true);

    // Discovers one assembly's embedded catalogs and registers it as a satellite source if marked. All
    // reflection (attribute reads, resource reads) happens before the lock; _gate is taken only to commit
    // into the lists and rebuild. Already-seen assemblies are skipped (the under-lock _seen.Add is the gate,
    // so two threads discovering the same assembly never double-add).
    private void DiscoverAssembly(Assembly assembly, bool rebuild)
    {
        lock (_gate)
        {
            if (_seen.Contains(assembly))
            {
                return;
            }
        }

        var isSatellite = HasSatelliteMarker(assembly);
        List<Catalog> embedded = ProduceEmbedded(assembly);

        var registeredSatellite = false;
        lock (_gate)
        {
            if (!_seen.Add(assembly))
            {
                return;
            }

            var changed = false;
            if (embedded.Count > 0)
            {
                _embeddedCatalogs.AddRange(embedded);
                changed = true;
            }

            if (isSatellite)
            {
                _satelliteAssemblies.Add(assembly);
                _hasSatellites = true;
                registeredSatellite = true;
            }

            if (changed && rebuild)
            {
                Rebuild();
            }
        }

        // Load this assembly's satellites for any culture already in use (reflection outside the lock).
        if (registeredSatellite)
        {
            ReconcileSatellites();
        }
    }

    // Loads every (satellite assembly × in-use culture) pair that has not been loaded yet. Production
    // (GetSatelliteAssembly + resource reads) runs outside _gate; _gate is taken only to find the missing
    // pairs and to commit. It loops until stable, so a satellite assembly and a culture appearing on
    // different threads at the same time are always reconciled (each pair is attempted exactly once).
    private void ReconcileSatellites()
    {
        while (true)
        {
            var missing = new List<(Assembly Assembly, string Culture)>();
            lock (_gate)
            {
                foreach (Assembly assembly in _satelliteAssemblies)
                {
                    foreach (var cultureName in _loadedCultures)
                    {
                        if (!_satellitePairs.Contains((assembly, cultureName)))
                        {
                            missing.Add((assembly, cultureName));
                        }
                    }
                }
            }

            if (missing.Count == 0)
            {
                return;
            }

            // Resolve the cultures and read the satellites outside the lock (GetCultureInfo and
            // GetSatelliteAssembly do nontrivial work and take the loader lock).
            var produced = new List<(Assembly Assembly, string Culture, List<Catalog> Catalogs)>(missing.Count);
            foreach ((Assembly assembly, var cultureName) in missing)
            {
                produced.Add((assembly, cultureName, ProduceSatellites(assembly, CultureInfo.GetCultureInfo(cultureName))));
            }

            lock (_gate)
            {
                var changed = false;
                foreach ((Assembly assembly, var culture, List<Catalog> catalogs) in produced)
                {
                    // Mark the pair attempted even when empty, so a culture a library does not translate is
                    // not retried; only add catalogs (and rebuild) when the satellite actually had some.
                    if (_satellitePairs.Add((assembly, culture)) && catalogs.Count > 0)
                    {
                        _satelliteCatalogs.AddRange(catalogs);
                        changed = true;
                    }
                }

                if (changed)
                {
                    Rebuild();
                }
            }
        }
    }

    private static bool HasSatelliteMarker(Assembly assembly)
    {
        try
        {
            return assembly.GetCustomAttribute<LocalizationSatelliteCatalogsAttribute>() is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Reads an assembly's embedded catalogs declared by [LocalizationCatalog]. Pure: no shared state, no
    // lock — the caller commits the result.
    private List<Catalog> ProduceEmbedded(Assembly assembly)
    {
        var catalogs = new List<Catalog>();
        foreach (LocalizationCatalogAttribute attribute in SafeAttributes(assembly))
        {
            Catalog? catalog = TryLoad(assembly, attribute);
            if (catalog is not null)
            {
                catalogs.Add(catalog);
            }
        }

        return catalogs;
    }

    // Reads the catalogs from an assembly's satellite for a culture. Pure: GetSatelliteAssembly and the
    // resource reads (which take the CLR loader lock) run here, outside any lock; the caller commits.
    private List<Catalog> ProduceSatellites(Assembly assembly, CultureInfo culture)
    {
        Assembly? satellite;
        try
        {
            satellite = assembly.GetSatelliteAssembly(culture);
        }
        catch (Exception)
        {
            // No satellite for this culture; that is the normal "this assembly doesn't ship this language" case.
            return [];
        }

        var catalogs = new List<Catalog>();
        foreach (var name in satellite.GetManifestResourceNames())
        {
            ITranslationFormat? format = _registry.ResolveByExtension(Path.GetExtension(name));
            if (format is null)
            {
                continue;
            }

            Catalog? catalog = ReadResource(satellite, name, format);
            if (catalog is not null)
            {
                catalogs.Add(catalog);
            }
        }

        return catalogs;
    }

    private static IEnumerable<LocalizationCatalogAttribute> SafeAttributes(Assembly assembly)
    {
        try
        {
            return assembly.GetCustomAttributes<LocalizationCatalogAttribute>();
        }
        catch (Exception)
        {
            // A dynamic or reflection-only assembly may refuse attribute access; skip it.
            return [];
        }
    }

    private Catalog? TryLoad(Assembly assembly, LocalizationCatalogAttribute attribute)
    {
        ITranslationFormat? format = _registry.ResolveById(attribute.Format);
        if (format is null)
        {
            return null;
        }

        try
        {
            using Stream? stream = assembly.GetManifestResourceStream(attribute.ResourceName);
            return stream is null
                ? null
                : format.ReadAsync(stream, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // A malformed or missing embedded catalog must never take down the host; skip it.
            return null;
        }
    }

    private static Catalog? ReadResource(Assembly assembly, string resourceName, ITranslationFormat format)
    {
        try
        {
            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            return stream is null
                ? null
                : format.ReadAsync(stream, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Rebuilds the merged snapshot from the current layers and swaps it in atomically. Callers hold _gate so
    // the lists are consistent; the swap itself is a single volatile write, so lookups never tear. Layered
    // low-to-high precedence: embedded and satellite (library-shipped) < directory (app-local files) < host
    // (explicit AddCatalog). A later catalog wins on overlap inside the merge.
    private void Rebuild()
    {
        List<Catalog> all = [.. _embeddedCatalogs, .. _satelliteCatalogs, .. _directoryCatalogs, .. _hostCatalogs];
        TranslationSnapshot snapshot = CatalogLoader.BuildSnapshot(all, Options);
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

    private void StartWatching()
    {
        if (!Directory.Exists(_directory))
        {
            return;
        }

        _debounce = new Timer(_ => Reload(), state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _watcher = new FileSystemWatcher(_directory) { EnableRaisingEvents = true };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Deleted += OnChanged;
    }

    private void OnChanged(object sender, FileSystemEventArgs e) =>
        _debounce?.Change(_hotReloadDebounce, Timeout.InfiniteTimeSpan);

    private static string DefaultDirectory() => Path.Combine(AppContext.BaseDirectory, "Translations");

    private static TranslationFormatRegistry BuildRegistry()
    {
        var registry = new TranslationFormatRegistry();
        registry.Register(new ArbTranslationFormat());
        registry.Register(new XliffTranslationFormat());
        registry.Register(new PoTranslationFormat());
        return registry;
    }
}
