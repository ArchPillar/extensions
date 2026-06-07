using System.Globalization;
using System.Reflection;
using ArchPillar.Extensions.Localization.Formats;

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
    private static readonly object _gate = new();
    private static readonly List<Catalog> _embeddedCatalogs = [];
    private static readonly List<Catalog> _directoryCatalogs = [];
    private static readonly List<Catalog> _hostCatalogs = [];
    private static readonly List<ITranslationSource> _sources = [];
    private static readonly HashSet<Assembly> _seen = [];
    private static readonly TranslationFormatRegistry _registry = BuildRegistry();
    private static string _sourceCulture = "en";
    private static string _directory = DefaultDirectory();
    private static bool _subscribed;
    private static bool _scannedLoaded;
    private static Localizer _current = new([], new LocalizerOptions());

    /// <summary>The global-namespace ambient localizer (the uncategorized bucket).</summary>
    public static ILocalizer Default { get; } = new AmbientLocalizer();

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
    /// The directory loaded by default (the dev/default "files beside the binary" path), defaulting to a
    /// <c>Translations</c> folder next to the application binary. Catalogs found here layer below host
    /// additions and above embedded catalogs. Setting it reloads the directory layer.
    /// </summary>
    public static string TranslationsDirectory
    {
        get => _directory;
        set
        {
            lock (_gate)
            {
                _directory = value ?? string.Empty;
                if (_scannedLoaded)
                {
                    _directoryCatalogs.Clear();
                    LoadDirectory(_directory);
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
    public static ILocalizer<T> For<T>() => new AmbientCategoryLocalizer<T>();

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
            _directoryCatalogs.Clear();
            _hostCatalogs.Clear();
            _sources.Clear();
            _seen.Clear();
            _sourceCulture = "en";
            _directory = DefaultDirectory();
            _scannedLoaded = false;
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

    private static void EnsureStarted()
    {
        if (Volatile.Read(ref _scannedLoaded))
        {
            return;
        }

        lock (_gate)
        {
            if (_scannedLoaded)
            {
                return;
            }

            if (!_subscribed)
            {
                _subscribed = true;
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            }

            LoadDirectory(_directory);
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                DiscoverEmbedded(assembly);
            }

            Rebuild();
            _scannedLoaded = true;
        }
    }

    private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
    {
        lock (_gate)
        {
            if (DiscoverEmbedded(args.LoadedAssembly))
            {
                Rebuild();
            }
        }
    }

    private static bool DiscoverEmbedded(Assembly assembly)
    {
        if (!_seen.Add(assembly))
        {
            return false;
        }

        var added = false;
        foreach (LocalizationCatalogAttribute attribute in SafeAttributes(assembly))
        {
            Catalog? catalog = TryLoad(assembly, attribute);
            if (catalog is not null)
            {
                _embeddedCatalogs.Add(catalog);
                added = true;
            }
        }

        return added;
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

    private static void Rebuild()
    {
        // Layered low-to-high precedence: embedded (library-shipped) < directory (app-local files) < host
        // (explicit AddCatalog). A later catalog wins on overlap inside the merge.
        List<Catalog> all = [.. _embeddedCatalogs, .. _directoryCatalogs, .. _hostCatalogs];
        var options = new LocalizerOptions { SourceCulture = _sourceCulture, Sources = [.. _sources] };
        var next = new Localizer(all, options);
        Interlocked.Exchange(ref _current, next).Dispose();
    }

    private static void LoadDirectory(string directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            Catalog? catalog = TryReadFile(file);
            if (catalog is not null)
            {
                _directoryCatalogs.Add(catalog);
            }
        }
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

    private static string DefaultDirectory() => Path.Combine(AppContext.BaseDirectory, "Translations");

    private static TranslationFormatRegistry BuildRegistry()
    {
        var registry = new TranslationFormatRegistry();
        registry.Register(new ArbTranslationFormat());
        registry.Register(new XliffTranslationFormat());
        registry.Register(new PoTranslationFormat());
        return registry;
    }

    private sealed class AmbientLocalizer : ILocalizer
    {
        public string Translate(string key, string defaultMessage, params (string Name, object? Value)[] arguments) =>
            Current.Translate(CultureInfo.CurrentUICulture, key, defaultMessage, context: null, arguments);

        public string Translate(string key, string defaultMessage, string context, params (string Name, object? Value)[] arguments) =>
            Current.Translate(CultureInfo.CurrentUICulture, key, defaultMessage, context, arguments);
    }

    private sealed class AmbientCategoryLocalizer<T> : ILocalizer<T>
    {
        private static readonly string _category = typeof(T).FullName ?? typeof(T).Name;

        public string Translate(string key, string defaultMessage, params (string Name, object? Value)[] arguments) =>
            Current.TranslateInCategory(_category, key, defaultMessage, context: null, arguments);

        public string Translate(string key, string defaultMessage, string context, params (string Name, object? Value)[] arguments) =>
            Current.TranslateInCategory(_category, key, defaultMessage, context, arguments);
    }
}
