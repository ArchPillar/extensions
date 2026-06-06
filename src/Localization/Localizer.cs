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
public sealed class Localizer : IDisposable
{
    private readonly LocalizerOptions _options;
    private readonly MessageFormatter _formatter;
    private readonly CultureInfo _sourceCulture;
    private readonly object _reloadGate = new();
    private TranslationSnapshot _snapshot;
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;

    /// <summary>
    /// Initializes a new instance of the <see cref="Localizer"/> class, loading catalogs immediately.
    /// </summary>
    /// <param name="options">The localizer configuration.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public Localizer(LocalizerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _formatter = new MessageFormatter(_options.MissingArguments);
        _sourceCulture = CreateCulture(_options.SourceCulture);
        _snapshot = CatalogLoader.Load(_options);
        if (_options.EnableHotReload)
        {
            StartWatching();
        }
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
        Translate(CultureInfo.CurrentUICulture, key, defaultMessage, context: null, arguments);

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
        Translate(CultureInfo.CurrentUICulture, key, defaultMessage, context, arguments);

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
        string key,
        string defaultMessage,
        string? context,
        params (string Name, object? Value)[] arguments)
    {
        if (culture is null)
        {
            throw new ArgumentNullException(nameof(culture));
        }

        var composite = TranslationKey.Compose(key, context);
        var message = Resolve(culture, composite);

        // An override was authored for the requested culture, so render it with that culture's rules.
        // The in-code default is source-language text, so render it with the source culture's rules —
        // otherwise an English default shown under, say, Japanese rules would pluralize incorrectly.
        return message is not null
            ? _formatter.Format(message, culture, arguments)
            : _formatter.Format(defaultMessage, _sourceCulture, arguments);
    }

    /// <summary>
    /// Rebuilds the loaded snapshot from the translations directory and swaps it in atomically.
    /// </summary>
    public void Reload()
    {
        lock (_reloadGate)
        {
            TranslationSnapshot snapshot = CatalogLoader.Load(_options);
            Volatile.Write(ref _snapshot, snapshot);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce?.Dispose();
    }

    private string? Resolve(CultureInfo culture, string compositeKey)
    {
        TranslationSnapshot snapshot = Volatile.Read(ref _snapshot);
        CultureInfo? current = culture;
        while (current is not null && !string.IsNullOrEmpty(current.Name))
        {
            if (snapshot.ByCulture.TryGetValue(current.Name, out IReadOnlyDictionary<string, string>? map)
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
