using System.Globalization;
using ArchPillar.Extensions.Localization.Formats;
using ArchPillar.Extensions.Localization.Internal;

namespace ArchPillar.Extensions.Localization;

/// <summary>
/// An <see cref="ICatalogProvider"/> over a file-system directory: it scans the directory at construction and
/// lists the translation files whose extension a registered format recognizes as <see cref="CatalogDescriptor"/>s
/// (the culture read from the file name, the bytes opened synchronously through <see cref="File.OpenRead(string)"/>).
/// It is born ready — its descriptor inventory is known once constructed — and watches the directory for changes
/// under hot reload. Because listing and opening complete synchronously, the store can satisfy a live culture
/// switch from its synchronous lookup path without blocking.
/// </summary>
public sealed class DirectoryCatalogProvider : ICatalogProvider
{
    // The fixed format tie-breaker (most-preferred first): when one catalog exists in several formats, the
    // higher-fidelity file wins and the loser is never opened. A precedence, not a configuration knob.
    private static readonly IReadOnlyList<string> _formatPrecedence = ["xliff", "arb", "po"];
    private readonly string _directory;
    private readonly TimeSpan _debounce;
    private readonly TranslationFormatRegistry _registry = BuiltInTranslationFormats.CreateRegistry();

    /// <summary>
    /// Initializes a new <see cref="DirectoryCatalogProvider"/> over <paramref name="directory"/>, scanning it now.
    /// </summary>
    /// <param name="directory">The directory containing the translation catalog files.</param>
    /// <param name="hotReloadDebounce">
    /// How long to let directory changes settle before a watch callback fires; defaults to 250&#160;ms.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="directory"/> is <see langword="null"/>.</exception>
    public DirectoryCatalogProvider(string directory, TimeSpan? hotReloadDebounce = null)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _debounce = hotReloadDebounce ?? TimeSpan.FromMilliseconds(250);
        Catalogs = Enumerate();
    }

    /// <inheritdoc />
    public IReadOnlyList<CatalogDescriptor> Catalogs { get; }

    /// <inheritdoc />
    public IReadOnlyList<CatalogDescriptor> CatalogsFor(CultureInfo culture)
    {
        if (culture is null)
        {
            throw new ArgumentNullException(nameof(culture));
        }

        return
        [
            .. Catalogs.Where(descriptor => string.Equals(descriptor.Culture, culture.Name, StringComparison.OrdinalIgnoreCase))
        ];
    }

    /// <inheritdoc />
    public IDisposable Watch(Action<CatalogDescriptor> onChanged)
    {
        if (onChanged is null)
        {
            throw new ArgumentNullException(nameof(onChanged));
        }

        if (!Directory.Exists(_directory))
        {
            return NoOpWatch.Instance;
        }

        return new DirectoryWatch(_directory, _debounce, DescribeWinner, onChanged);
    }

    // Scans the directory once, listing one descriptor per logical catalog (the file name without its format
    // extension): when a catalog exists in several formats, only the highest-precedence one is listed, so a losing
    // format is never opened. Files no registered format recognizes are skipped.
    private List<CatalogDescriptor> Enumerate()
    {
        if (!Directory.Exists(_directory))
        {
            return [];
        }

        var winners = new Dictionary<string, CatalogDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(_directory))
        {
            if (Describe(file) is not { } descriptor)
            {
                continue;
            }

            var key = Path.GetFileNameWithoutExtension(file);
            if (!winners.TryGetValue(key, out CatalogDescriptor? existing) || Rank(descriptor.Format) < Rank(existing.Format))
            {
                winners[key] = descriptor;
            }
        }

        return [.. winners.Values];
    }

    // Builds the descriptor for one file, or null when no registered format recognizes its extension. Shared by
    // the construction-time scan and the watch, so a watched file is described by the exact same per-file rule.
    private CatalogDescriptor? Describe(string path)
    {
        var extension = Path.GetExtension(path);
        if (_registry.ResolveByExtension(extension) is null)
        {
            return null;
        }

        var filePath = path;
        return new CatalogDescriptor
        {
            Culture = CultureFromFileName(path),
            Format = extension,
            Name = Path.GetFileName(path),
            Source = new CatalogSource.Synchronous(() => File.OpenRead(filePath))
        };
    }

    // The current winning descriptor for the logical catalog a changed file belongs to, re-evaluated against the
    // directory so a change to a shadowed (lower-precedence) file resolves to the winner, not the changed file.
    // Falls back to the changed path when its group is now empty, so the store drops a catalog whose files are gone.
    private CatalogDescriptor? DescribeWinner(string path)
    {
        if (_registry.ResolveByExtension(Path.GetExtension(path)) is null)
        {
            return null;
        }

        var key = Path.GetFileNameWithoutExtension(path);
        CatalogDescriptor? winner = null;
        if (Directory.Exists(_directory))
        {
            foreach (var file in Directory.EnumerateFiles(_directory))
            {
                if (!string.Equals(Path.GetFileNameWithoutExtension(file), key, StringComparison.OrdinalIgnoreCase) || Describe(file) is not { } descriptor)
                {
                    continue;
                }

                if (winner is null || Rank(descriptor.Format) < Rank(winner.Format))
                {
                    winner = descriptor;
                }
            }
        }

        return winner ?? Describe(path);
    }

    // A format's precedence rank by its registered id (lower wins); an unrecognized or unranked format sorts last,
    // so it loses to any ranked format present for the same catalog.
    private int Rank(string format)
    {
        ITranslationFormat? resolved = _registry.ResolveByExtension(format);
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

    // The culture tag a catalog file name ends with: App.Web.de.xliff -> "de", de.arb -> "de". The same rule
    // the directory loader uses, keyed off the {name}.{culture}.{ext} naming convention.
    private static string CultureFromFileName(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var lastDot = name.LastIndexOf('.');
        return lastDot >= 0 ? name[(lastDot + 1)..] : name;
    }

    // A debounced FileSystemWatcher that accumulates the changed file paths: every change records the path and
    // resets a one-shot timer, so a burst of edits fires once, reporting each distinct changed path through the
    // callback. A path whose extension no format recognizes is skipped (Describe returns null). Disposes the
    // watcher (unsubscribed first) and the timer together.
    private sealed class DirectoryWatch : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly Timer _timer;
        private readonly TimeSpan _debounce;
        private readonly Func<string, CatalogDescriptor?> _describe;
        private readonly Action<CatalogDescriptor> _onChanged;
        private readonly object _gate = new();
        private readonly HashSet<string> _pending = new(StringComparer.Ordinal);

        public DirectoryWatch(string directory, TimeSpan debounce, Func<string, CatalogDescriptor?> describe, Action<CatalogDescriptor> onChanged)
        {
            _debounce = debounce;
            _describe = describe;
            _onChanged = onChanged;
            _timer = new Timer(_ => Fire(), state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _watcher = new FileSystemWatcher(directory) { EnableRaisingEvents = true };
            _watcher.Changed += OnChanged;
            _watcher.Created += OnChanged;
            _watcher.Deleted += OnChanged;
            _watcher.Renamed += OnRenamed;
        }

        public void Dispose()
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnChanged;
            _watcher.Created -= OnChanged;
            _watcher.Deleted -= OnChanged;
            _watcher.Renamed -= OnRenamed;
            _watcher.Dispose();
            _timer.Dispose();
        }

        private void OnChanged(object sender, FileSystemEventArgs e) => Accumulate(e.FullPath);

        private void OnRenamed(object sender, RenamedEventArgs e)
        {
            Accumulate(e.OldFullPath);
            Accumulate(e.FullPath);
        }

        private void Accumulate(string path)
        {
            lock (_gate)
            {
                _pending.Add(path);
            }

            _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
        }

        private void Fire()
        {
            string[] paths;
            lock (_gate)
            {
                paths = [.. _pending];
                _pending.Clear();
            }

            foreach (var path in paths)
            {
                if (_describe(path) is { } descriptor)
                {
                    _onChanged(descriptor);
                }
            }
        }
    }
}
