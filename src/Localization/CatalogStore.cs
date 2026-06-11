using ArchPillar.Extensions.Localization.Internal;

namespace ArchPillar.Extensions.Localization;

/// <summary>
/// Owns a set of translation catalogs loaded from a directory and keeps them current: it reads the configured
/// <see cref="LocalizerOptions.TranslationsDirectory"/>, optionally watches it and reloads on change, and
/// exposes the merged snapshot for a <see cref="DefaultLocalizer"/> to resolve against. It is a catalogue source, not
/// a localizer — resolution and formatting belong to the engine, which reads this store's current snapshot.
/// </summary>
public sealed class CatalogStore : IDisposable
{
    private readonly object _reloadGate = new();
    private TranslationSnapshot _snapshot;
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;

    /// <summary>
    /// Initializes a new <see cref="CatalogStore"/> over <paramref name="options"/>, loading the configured
    /// directory immediately and watching it for changes when <see cref="LocalizerOptions.EnableHotReload"/>
    /// is set.
    /// </summary>
    /// <param name="options">The catalogue configuration.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public CatalogStore(LocalizerOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        _snapshot = CatalogLoader.Load(Options);
        if (Options.EnableHotReload)
        {
            StartWatching();
        }
    }

    /// <summary>The configuration this store was built with, so a localizer over it shares the same source
    /// culture, missing-argument policy, and dynamic sources.</summary>
    internal LocalizerOptions Options { get; }

    /// <summary>The current merged snapshot; replaced atomically on reload, so a reader always sees a
    /// consistent view and a resolving localizer observes a reload on its next lookup.</summary>
    internal TranslationSnapshot Snapshot => Volatile.Read(ref _snapshot);

    /// <summary>Rebuilds the snapshot from the translations directory and swaps it in atomically.</summary>
    public void Reload()
    {
        lock (_reloadGate)
        {
            TranslationSnapshot snapshot = CatalogLoader.Load(Options);
            Volatile.Write(ref _snapshot, snapshot);
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

    private void StartWatching()
    {
        if (!Directory.Exists(Options.TranslationsDirectory))
        {
            return;
        }

        _debounce = new Timer(_ => Reload(), state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _watcher = new FileSystemWatcher(Options.TranslationsDirectory) { EnableRaisingEvents = true };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Deleted += OnChanged;
    }

    private void OnChanged(object sender, FileSystemEventArgs e) =>
        _debounce?.Change(Options.HotReloadDebounce, Timeout.InfiniteTimeSpan);
}
