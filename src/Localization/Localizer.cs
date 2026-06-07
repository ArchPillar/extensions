using System.Globalization;
using ArchPillar.Extensions.Localization.Internal;
using ArchPillar.Extensions.Localization.MessageFormat;

namespace ArchPillar.Extensions.Localization;

/// <summary>
/// Renders translatable call sites at runtime: looks up the loaded override for the requested culture
/// and key, falls back through parent cultures to the in-code default, and formats with the ICU engine.
/// Lookups are lock-free; loading is pluggable and optionally hot-reloadable. Designed to be a singleton
/// and safe for concurrent use.
/// </summary>
public sealed class Localizer : IDisposable, ILocalizer
{
    private readonly LocalizerOptions _options;
    private readonly Func<TranslationSnapshot> _load;
    private readonly IReadOnlyList<ITranslationSource> _sources;
    private readonly MessageFormatter _formatter;
    private readonly CultureInfo _sourceCulture;
    private readonly object _reloadGate = new();
    private TranslationSnapshot _snapshot;
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;

    /// <summary>
    /// Initializes a new instance of the <see cref="Localizer"/> class, loading catalogs from the
    /// configured directory immediately.
    /// </summary>
    /// <param name="options">The localizer configuration.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public Localizer(LocalizerOptions options)
        : this(options ?? throw new ArgumentNullException(nameof(options)), () => CatalogLoader.Load(options!))
    {
        if (_options.EnableHotReload)
        {
            StartWatching();
        }
    }

    /// <summary>
    /// Initializes an isolated localizer over the given <paramref name="catalogs"/>, bypassing the
    /// translations directory and the ambient store. Intended for tests and self-contained or multi-tenant
    /// scenarios. The source-language catalog and untranslated entries are skipped exactly as the directory
    /// loader does; on per-culture overlap, later catalogs win. Hot reload does not apply.
    /// </summary>
    /// <param name="catalogs">The parsed catalogs to load as overrides.</param>
    /// <param name="options">The localizer configuration, or <see langword="null"/> for the defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="catalogs"/> is <see langword="null"/>.</exception>
    public Localizer(IEnumerable<Catalog> catalogs, LocalizerOptions? options = null)
        : this(options ?? new LocalizerOptions(), catalogs ?? throw new ArgumentNullException(nameof(catalogs)))
    {
    }

    /// <summary>
    /// Initializes an isolated localizer over a single <paramref name="catalog"/>, bypassing the
    /// translations directory and the ambient store.
    /// </summary>
    /// <param name="catalog">The parsed catalog to load as overrides.</param>
    /// <param name="options">The localizer configuration, or <see langword="null"/> for the defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="catalog"/> is <see langword="null"/>.</exception>
    public Localizer(Catalog catalog, LocalizerOptions? options = null)
        : this(Single(catalog), options)
    {
    }

    private Localizer(LocalizerOptions options, IEnumerable<Catalog> catalogs)
        : this(options, BuildCatalogLoader(catalogs, options))
    {
    }

    private Localizer(LocalizerOptions options, Func<TranslationSnapshot> load)
    {
        _options = options;
        _load = load;
        _sources = options.Sources ?? [];
        _formatter = new MessageFormatter(_options.MissingArguments);
        _sourceCulture = CreateCulture(_options.SourceCulture);
        _snapshot = load();
    }

    /// <summary>
    /// Creates a <see cref="Localizer"/> from catalogs the caller has already loaded — for hosts without a
    /// readable file system (such as Blazor WebAssembly), where the catalogs are fetched over HTTP and
    /// parsed with an <see cref="ITranslationFormat"/> before being handed in here. Equivalent to the
    /// <see cref="Localizer(IEnumerable{Catalog}, LocalizerOptions?)"/> constructor.
    /// </summary>
    /// <param name="catalogs">The parsed catalogs to load as overrides.</param>
    /// <param name="options">The localizer configuration, or <see langword="null"/> for the defaults.</param>
    /// <returns>A localizer backed by the supplied catalogs.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="catalogs"/> is <see langword="null"/>.</exception>
    public static Localizer FromCatalogs(IEnumerable<Catalog> catalogs, LocalizerOptions? options = null) =>
        new(catalogs ?? throw new ArgumentNullException(nameof(catalogs)), options);

    private static IEnumerable<Catalog> Single(Catalog catalog) =>
        catalog is null ? throw new ArgumentNullException(nameof(catalog)) : [catalog];

    private static Func<TranslationSnapshot> BuildCatalogLoader(IEnumerable<Catalog> catalogs, LocalizerOptions options)
    {
        List<Catalog> source = [.. catalogs];
        return () => CatalogLoader.BuildSnapshot(source, options);
    }

    /// <summary>
    /// Translates <paramref name="key"/> for the current UI culture, falling back to
    /// <paramref name="defaultMessage"/>.
    /// </summary>
    /// <param name="key">The stable symbolic key.</param>
    /// <param name="defaultMessage">The in-code source default (ICU MessageFormat).</param>
    /// <param name="arguments">The message arguments as <c>(name, value)</c> tuples.</param>
    /// <returns>The rendered string.</returns>
    public string Translate(
        [Translatable] string key,
        [TranslationDefault] string defaultMessage,
        params (string Name, object? Value)[] arguments) =>
        TranslateCore(CultureInfo.CurrentUICulture, key, defaultMessage, context: null, arguments);

    /// <summary>
    /// Translates <paramref name="key"/> with a disambiguation <paramref name="context"/> for the current
    /// UI culture, falling back to <paramref name="defaultMessage"/>.
    /// </summary>
    /// <param name="key">The stable symbolic key.</param>
    /// <param name="defaultMessage">The in-code source default (ICU MessageFormat).</param>
    /// <param name="context">The disambiguation context.</param>
    /// <param name="arguments">The message arguments as <c>(name, value)</c> tuples.</param>
    /// <returns>The rendered string.</returns>
    public string Translate(
        [Translatable] string key,
        [TranslationDefault] string defaultMessage,
        [TranslationContext] string context,
        params (string Name, object? Value)[] arguments) =>
        TranslateCore(CultureInfo.CurrentUICulture, key, defaultMessage, context, arguments);

    /// <summary>
    /// Translates <paramref name="key"/> for an explicit <paramref name="culture"/>, falling back through
    /// parent cultures to <paramref name="defaultMessage"/>.
    /// </summary>
    /// <param name="culture">The culture to translate for.</param>
    /// <param name="key">The stable symbolic key.</param>
    /// <param name="defaultMessage">The in-code source default (ICU MessageFormat).</param>
    /// <param name="context">The optional disambiguation context.</param>
    /// <param name="arguments">The message arguments as <c>(name, value)</c> tuples.</param>
    /// <returns>The rendered string.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="culture"/> is <see langword="null"/>.</exception>
    public string Translate(
        CultureInfo culture,
        [Translatable] string key,
        [TranslationDefault] string defaultMessage,
        [TranslationContext] string? context,
        params (string Name, object? Value)[] arguments) =>
        TranslateCore(culture, key, defaultMessage, context, arguments);

    /// <summary>
    /// Translates for an explicit culture and additionally reports whether a loaded override was used
    /// (rather than the in-code default). Intended for integration adapters such as <c>IStringLocalizer</c>.
    /// </summary>
    /// <param name="culture">The culture to translate for.</param>
    /// <param name="key">The stable symbolic key.</param>
    /// <param name="defaultMessage">The fallback rendered when no override exists.</param>
    /// <param name="context">The optional disambiguation context.</param>
    /// <param name="overrideFound">Set to <see langword="true"/> when a loaded override was used.</param>
    /// <param name="arguments">The message arguments as <c>(name, value)</c> tuples.</param>
    /// <returns>The rendered string.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="culture"/> is <see langword="null"/>.</exception>
    public string Translate(
        CultureInfo culture,
        string key,
        string defaultMessage,
        string? context,
        out bool overrideFound,
        params (string Name, object? Value)[] arguments)
    {
        if (culture is null)
        {
            throw new ArgumentNullException(nameof(culture));
        }

        var composite = TranslationKey.Compose(key, context);
        var message = ResolveOverride(culture, category: string.Empty, composite, defaultMessage);
        overrideFound = message is not null;

        // An override was authored for the requested culture, so render it with that culture's rules.
        // The in-code default is source-language text, so render it with the source culture's rules —
        // otherwise an English default shown under, say, Japanese rules would pluralize incorrectly.
        return message is not null
            ? _formatter.Format(message, culture, arguments)
            : _formatter.Format(defaultMessage, _sourceCulture, arguments);
    }

    // The category-scoped core used by ILocalizer<T> (via the factory). It looks the key up within the
    // localizer's category for the current UI culture, falling back to the in-code default. A literal
    // lookup with no context allocates nothing: the composite key is the key itself and the tiered
    // dictionary reads do not allocate.
    internal string TranslateInCategory(
        string category,
        string key,
        string defaultMessage,
        string? context,
        (string Name, object? Value)[] arguments)
    {
        CultureInfo culture = CultureInfo.CurrentUICulture;
        var composite = TranslationKey.Compose(key, context);
        var message = ResolveOverride(culture, category, composite, defaultMessage);
        return message is not null
            ? _formatter.Format(message, culture, arguments)
            : _formatter.Format(defaultMessage, _sourceCulture, arguments);
    }

    // Consults the custom sources (a later source wins) before the loaded catalogs. When no sources are
    // configured the loop is skipped and the catalog lookup is unchanged, so the literal path stays
    // allocation-free.
    private string? ResolveOverride(CultureInfo culture, string category, string compositeKey, string defaultMessage)
    {
        for (var index = _sources.Count - 1; index >= 0; index--)
        {
            var fromSource = _sources[index].Resolve(culture, category, compositeKey, defaultMessage);
            if (fromSource is not null)
            {
                return fromSource;
            }
        }

        return Resolve(culture, category, compositeKey);
    }

    // The non-attributed core. The public overloads carry the attributes so the extractor finds every
    // call site; they delegate here so the library's own forwarding never looks like a translation site.
    private string TranslateCore(
        CultureInfo culture,
        string key,
        string defaultMessage,
        string? context,
        (string Name, object? Value)[] arguments) =>
        Translate(culture, key, defaultMessage, context, out _, arguments);

    /// <summary>
    /// Rebuilds the loaded snapshot from its source (the translations directory, or the supplied catalogs)
    /// and swaps it in atomically.
    /// </summary>
    public void Reload()
    {
        lock (_reloadGate)
        {
            TranslationSnapshot snapshot = _load();
            Volatile.Write(ref _snapshot, snapshot);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce?.Dispose();
    }

    private string? Resolve(CultureInfo culture, string category, string compositeKey)
    {
        TranslationSnapshot snapshot = Volatile.Read(ref _snapshot);
        CultureInfo? current = culture;
        while (current is not null && !string.IsNullOrEmpty(current.Name))
        {
            if (snapshot.ByCulture.TryGetValue(current.Name, out IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? byCategory)
                && byCategory.TryGetValue(category, out IReadOnlyDictionary<string, string>? map)
                && map.TryGetValue(compositeKey, out var message))
            {
                return message;
            }

            current = current.Parent;
        }

        return null;
    }

    private void StartWatching()
    {
        if (!Directory.Exists(_options.TranslationsDirectory))
        {
            return;
        }

        _debounce = new Timer(_ => Reload(), state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _watcher = new FileSystemWatcher(_options.TranslationsDirectory) { EnableRaisingEvents = true };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Deleted += OnChanged;
    }

    private void OnChanged(object sender, FileSystemEventArgs e) =>
        _debounce?.Change(_options.HotReloadDebounce, Timeout.InfiniteTimeSpan);

    private static CultureInfo CreateCulture(string name)
    {
        try
        {
            return CultureInfo.GetCultureInfo(name);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}
