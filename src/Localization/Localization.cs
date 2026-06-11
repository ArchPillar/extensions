using System.Globalization;
using System.Reflection;
using ArchPillar.Extensions.Localization.Formats;
using ArchPillar.Extensions.Localization.MessageFormat;

namespace ArchPillar.Extensions.Localization;

/// <summary>
/// The process-wide ambient translation store, modeled on <c>IConfiguration</c>: one store built from
/// layered sources where a later source wins, reachable with no services so a string can be localized
/// from anywhere — including an exception message thrown before any container exists. A library's embedded
/// catalogs and a host's overrides are both just sources layered in. Embedded catalogs are discovered
/// lazily as assemblies load (an assembly cannot run a translatable call before it is loaded), so nothing
/// has to be configured for a library's translations to work.
/// </summary>
public static class Localization
{
    // _gate guards ONLY the in-memory bookkeeping: the catalog lists, the discovery sets, and the snapshot
    // rebuild/swap — all short, allocation-light operations. Reflection and I/O (reading assembly attributes,
    // GetSatelliteAssembly, reading embedded resources, reading files) are NEVER done while holding _gate.
    // That is deliberate: those operations take the CLR loader lock, and the AssemblyLoad handler runs while
    // the loader lock is held. If _gate were held across a loader-lock operation, the handler taking _gate
    // would invert the lock order and deadlock. Keeping _gate off the reflection paths makes that impossible.
    private static readonly object _gate = new();
    private static readonly object _startupGate = new();
    private static readonly List<Catalog> _embeddedCatalogs = [];
    private static readonly List<Catalog> _satelliteCatalogs = [];
    private static readonly List<Catalog> _directoryCatalogs = [];
    private static readonly List<Catalog> _hostCatalogs = [];
    private static readonly List<ITranslationSource> _sources = [];
    private static readonly List<Assembly> _satelliteAssemblies = [];
    private static readonly HashSet<Assembly> _seen = [];
    private static readonly HashSet<(Assembly Assembly, string Culture)> _satellitePairs = [];
    private static readonly TranslationFormatRegistry _registry = BuildRegistry();
    private static HashSet<string> _loadedCultures = new(StringComparer.OrdinalIgnoreCase);
    private static string _sourceCulture = "en";
    private static MissingArgumentPolicy _missingArguments = MissingArgumentPolicy.PassThrough;
    private static string _directory = DefaultDirectory();
    private static bool _subscribed;
    private static bool _scannedLoaded;
    private static volatile bool _hasSatellites;
    private static Localizer _current = new([], new LocalizerOptions());

    /// <summary>The global-namespace ambient localizer (the uncategorized bucket).</summary>
    public static ILocalizer Default { get; } = new Internal.AmbientLocalizer();

    /// <summary>The source culture used to render in-code defaults; defaults to <c>en</c>.</summary>
    public static string SourceCulture
    {
        get => _sourceCulture;
        set
        {
            lock (_gate)
            {
                _sourceCulture = value ?? "en";
                Rebuild();
            }
        }
    }

    /// <summary>
    /// How the formatter handles a message that references an argument no value was supplied for; defaults
    /// to <see cref="MissingArgumentPolicy.PassThrough"/>. Applied to every ambient-resolved translation, so
    /// an injected interface and a non-DI caller share the same policy as a directly constructed localizer.
    /// </summary>
    public static MissingArgumentPolicy MissingArguments
    {
        get => _missingArguments;
        set
        {
            lock (_gate)
            {
                _missingArguments = value;
                Rebuild();
            }
        }
    }

    /// <summary>
    /// The directory loaded by default (the dev/default "files beside the binary" path), defaulting to a
    /// <c>Translations</c> folder next to the application binary. Catalogs found here layer below host
    /// additions and above embedded catalogs. Setting it reloads the directory layer.
    /// </summary>
    public static string TranslationsDirectory
    {
        get => _directory;
        set
        {
            var directory = value ?? string.Empty;

            // Hold _startupGate so this cannot interleave with the one-time EnsureStarted: either startup
            // runs first (then this reloads), or this runs first (then startup reads the new directory).
            // That closes the race where a new directory is recorded but never read. I/O stays off _gate.
            lock (_startupGate)
            {
                lock (_gate)
                {
                    _directory = directory;
                }

                if (!Volatile.Read(ref _scannedLoaded))
                {
                    return;
                }

                List<Catalog> catalogs = ReadDirectory(directory);
                lock (_gate)
                {
                    _directoryCatalogs.Clear();
                    _directoryCatalogs.AddRange(catalogs);
                    Rebuild();
                }
            }
        }
    }

    /// <summary>
    /// Returns the ambient localizer scoped to <typeparamref name="T"/>'s full name, reading the latest
    /// store on every call.
    /// </summary>
    /// <typeparam name="T">The type whose full name is the translation category.</typeparam>
    /// <returns>The ambient category-scoped localizer.</returns>
    public static ILocalizer<T> For<T>() => new Internal.AmbientCategoryLocalizer<T>();

    /// <summary>
    /// Translates <paramref name="key"/> through the global ambient store, falling back to
    /// <paramref name="defaultMessage"/>. The free-function form of <see cref="Default"/>, for
    /// <c>using static ArchPillar.Extensions.Localization.Localization;</c> — the call site then reads
    /// <c>Translate("greeting", "Hello {name}!", ("name", name))</c> with no receiver.
    /// </summary>
    /// <param name="key">The stable symbolic key.</param>
    /// <param name="defaultMessage">The in-code source default (ICU MessageFormat).</param>
    /// <param name="arguments">The message arguments as <c>(name, value)</c> tuples.</param>
    /// <returns>The rendered string.</returns>
    public static string Translate(
        [Translatable] string key,
        [TranslationDefault] string defaultMessage,
        params (string Name, object? Value)[] arguments) =>
        Default.Translate(key, defaultMessage, arguments);

    /// <summary>
    /// Translates <paramref name="key"/> with a disambiguation <paramref name="context"/> through the global
    /// ambient store, falling back to <paramref name="defaultMessage"/>. The free-function form of
    /// <see cref="Default"/> for <c>using static</c>.
    /// </summary>
    /// <param name="key">The stable symbolic key.</param>
    /// <param name="defaultMessage">The in-code source default (ICU MessageFormat).</param>
    /// <param name="context">The disambiguation context.</param>
    /// <param name="arguments">The message arguments as <c>(name, value)</c> tuples.</param>
    /// <returns>The rendered string.</returns>
    public static string Translate(
        [Translatable] string key,
        [TranslationDefault] string defaultMessage,
        [TranslationContext] string context,
        params (string Name, object? Value)[] arguments) =>
        Default.Translate(key, defaultMessage, context, arguments);

    /// <summary>
    /// Layers a catalog into the store as a host source (a later source wins). Use this to override or add
    /// translations from the host.
    /// </summary>
    /// <param name="catalog">The catalog to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="catalog"/> is <see langword="null"/>.</exception>
    public static void AddCatalog(Catalog catalog)
    {
        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        lock (_gate)
        {
            _hostCatalogs.Add(catalog);
            Rebuild();
        }
    }

    /// <summary>
    /// Layers a dynamic source into the store (a later source wins) — for example a pseudo-localization
    /// source.
    /// </summary>
    /// <param name="source">The source to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public static void AddSource(ITranslationSource source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

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

    /// <summary>
    /// Clears all layered catalogs, sources, and discovery state, returning the store to empty. Intended
    /// for test isolation against the shared ambient state.
    /// </summary>
    public static void Reset()
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
            _sourceCulture = "en";
            _missingArguments = MissingArgumentPolicy.PassThrough;
            _directory = DefaultDirectory();
            _scannedLoaded = false;
            _hasSatellites = false;
            var empty = new Localizer([], new LocalizerOptions());
            Interlocked.Exchange(ref _current, empty).Dispose();
        }
    }

    internal static Localizer Current
    {
        get
        {
            EnsureStarted();
            return Volatile.Read(ref _current);
        }
    }

    // The found-aware ambient lookup the IStringLocalizer adapter composes over: resolves within
    // <paramref name="category"/> for the current UI culture and reports whether a loaded override was used,
    // so the adapter can fall through to a previously-registered factory on a miss before using the default.
    internal static string TranslateInCategory(
        string category,
        string key,
        string defaultMessage,
        (string Name, object? Value)[] arguments,
        out bool overrideFound)
    {
        CultureInfo culture = CultureInfo.CurrentUICulture;
        EnsureCulture(culture);
        return Current.TranslateInCategory(category, key, defaultMessage, context: null, out overrideFound, arguments);
    }

    // The override-or-null ambient lookup the IStringLocalizer adapter uses so it never renders the name as a
    // default before consulting the inner factory (the name may be ResourceManager composite-format text).
    internal static string? TranslateOverride(string category, string key, (string Name, object? Value)[] arguments)
    {
        EnsureCulture(CultureInfo.CurrentUICulture);
        return Current.TranslateOverride(category, key, context: null, arguments);
    }

    // Enumerates the loaded overrides for a category in the current UI culture, so the IStringLocalizer
    // adapter's GetAllStrings includes ambient entries rather than only the inner factory's.
    internal static IReadOnlyList<KeyValuePair<string, string>> EnumerateOverrides(string category, bool includeParentCultures)
    {
        CultureInfo culture = CultureInfo.CurrentUICulture;
        EnsureCulture(culture);
        return Current.EnumerateCategory(culture, category, includeParentCultures);
    }

    // One-time startup: subscribe to assembly loads, read the directory layer, and discover every
    // already-loaded assembly. _startupGate (not _gate) serializes this so concurrent first calls run it
    // once; the reflection and I/O inside run WITHOUT _gate, which is taken only for the short commits.
    private static void EnsureStarted()
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

            List<Catalog> directory = ReadDirectory(_directory);
            lock (_gate)
            {
                _directoryCatalogs.AddRange(directory);
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

    private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args) =>
        DiscoverAssembly(args.LoadedAssembly, rebuild: true);

    // Discovers one assembly's embedded catalogs and registers it as a satellite source if marked. All
    // reflection (attribute reads, resource reads) happens before the lock; _gate is taken only to commit
    // into the lists and rebuild. Already-seen assemblies are skipped (the under-lock _seen.Add is the gate,
    // so two threads discovering the same assembly never double-add).
    private static void DiscoverAssembly(Assembly assembly, bool rebuild)
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

    // Loads the satellite catalogs for a culture (and its parents) the first time the culture is used. The
    // fast path is a volatile bool plus a lock-free set read, so a files-only app pays almost nothing.
    internal static void EnsureCulture(CultureInfo culture)
    {
        EnsureStarted();
        if (!_hasSatellites)
        {
            return;
        }

        if (Volatile.Read(ref _loadedCultures).Contains(culture.Name))
        {
            return;
        }

        // Register the requested culture and its parents as "in use" (cheap, no reflection), then load their
        // satellites outside the lock via the reconcile.
        lock (_gate)
        {
            var loaded = new HashSet<string>(_loadedCultures, StringComparer.OrdinalIgnoreCase);
            for (CultureInfo? current = culture; current is not null && !string.IsNullOrEmpty(current.Name); current = current.Parent)
            {
                loaded.Add(current.Name);
            }

            Volatile.Write(ref _loadedCultures, loaded);
        }

        ReconcileSatellites();
    }

    // Loads every (satellite assembly × in-use culture) pair that has not been loaded yet. Production
    // (GetSatelliteAssembly + resource reads) runs outside _gate; _gate is taken only to find the missing
    // pairs and to commit. It loops until stable, so a satellite assembly and a culture appearing on
    // different threads at the same time are always reconciled (each pair is attempted exactly once).
    private static void ReconcileSatellites()
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
    private static List<Catalog> ProduceEmbedded(Assembly assembly)
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
    private static List<Catalog> ProduceSatellites(Assembly assembly, CultureInfo culture)
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

    private static Catalog? TryLoad(Assembly assembly, LocalizationCatalogAttribute attribute)
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

    // Rebuilds the merged snapshot from the current lists and swaps it in atomically. Callers hold _gate so
    // the lists are consistent; the swap itself is a single Interlocked.Exchange, so lookups never tear.
    private static void Rebuild()
    {
        // Layered low-to-high precedence: embedded and satellite (library-shipped) < directory (app-local
        // files) < host (explicit AddCatalog). A later catalog wins on overlap inside the merge.
        List<Catalog> all = [.. _embeddedCatalogs, .. _satelliteCatalogs, .. _directoryCatalogs, .. _hostCatalogs];
        var options = new LocalizerOptions { SourceCulture = _sourceCulture, Sources = [.. _sources], MissingArguments = _missingArguments };
        var next = new Localizer(all, options);
        Interlocked.Exchange(ref _current, next).Dispose();
    }

    // Reads every catalog file in a directory. Pure: file I/O only, no shared state, no lock.
    private static List<Catalog> ReadDirectory(string directory)
    {
        var catalogs = new List<Catalog>();
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return catalogs;
        }

        // Order by ordinal path so the directory layer is deterministic regardless of the file system's
        // enumeration order; on an overlapping key the later path wins.
        var files = new List<string>(Directory.EnumerateFiles(directory));
        files.Sort(string.CompareOrdinal);
        foreach (var file in files)
        {
            Catalog? catalog = TryReadFile(file);
            if (catalog is not null)
            {
                catalogs.Add(catalog);
            }
        }

        return catalogs;
    }

    private static Catalog? TryReadFile(string file)
    {
        ITranslationFormat? format = _registry.ResolveByExtension(Path.GetExtension(file));
        if (format is null)
        {
            return null;
        }

        try
        {
            using FileStream stream = File.OpenRead(file);
            return format.ReadAsync(stream, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // A malformed file must never take down the host; skip it.
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
