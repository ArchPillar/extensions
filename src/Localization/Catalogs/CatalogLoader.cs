using System.Collections.Concurrent;
using System.Text.Json;
using System.Xml;
using ArchPillar.Extensions.Localization.Providers;

namespace ArchPillar.Extensions.Localization.Catalogs;

/// <summary>
/// The one owner of loading catalogs and remembering what has been loaded. When asked to load a descriptor it
/// deduplicates the request itself — skipping any catalog already loaded, already failed, or in flight — then opens
/// it (a synchronous source inline, an asynchronous source on a coalesced background task, one fetch per identity)
/// and records the result under its provider. It signals through <c>onAsyncLanded</c> when an asynchronous catalog
/// grows the loaded set, so the store rebuilds its snapshot; it never publishes itself.
/// </summary>
/// <remarks>
/// Lock-free: the per-provider registry maps each identity to its loaded catalog, or <see langword="null"/> when the
/// load was attempted and failed (so it is not retried). The provider set is fixed for one configuration, so the
/// outer map's keys never change after construction and it is read concurrently without locking; the inner maps are
/// <see cref="ConcurrentDictionary{TKey,TValue}"/>, so dedup is an atomic <c>TryAdd</c>. Opens run without any lock
/// (they take the CLR loader lock).
/// </remarks>
internal sealed class CatalogLoader(
    IReadOnlyList<ICatalogProvider> providers)
{
    private readonly IReadOnlyDictionary<ICatalogProvider, ConcurrentDictionary<(string Culture, string Name), Catalog?>> _registry = providers.ToDictionary(provider => provider, _ => new ConcurrentDictionary<(string Culture, string Name), Catalog?>());
    private readonly ConcurrentDictionary<(string Culture, string Name), Lazy<Task>> _inFlight = new();

    // Loads the whole <paramref name="work"/> set (each item a provider and one of its descriptors): every catalog is
    // opened and registered unless already loaded, failed, or in flight. A synchronous source is opened inline and an
    // asynchronous one is coalesced onto the background queue. <paramref name="onChanged"/> is raised whenever the
    // loaded set actually grows — once for the synchronous batch, and once per asynchronous catalog as it lands —
    // regardless of timing. Returns the in-flight tasks so an awaited caller can drain them.
    public IReadOnlyList<Task> Load(
        IReadOnlyList<(ICatalogProvider Provider, CatalogDescriptor Descriptor)> work,
        Action onChanged)
    {
        var tasks = new List<Task>();
        var grew = false;
        foreach ((ICatalogProvider provider, CatalogDescriptor descriptor) in work)
        {
            switch (descriptor.Source)
            {
                case CatalogSource.Synchronous source:
                    grew |= OpenSynchronous(provider, descriptor, source);
                    break;
                case CatalogSource.Asynchronous source:
                    // Wrap the fetch in a Lazy so GetOrAdd's factory is side-effect-free: a racing GetOrAdd may build
                    // several Lazy values, but only the one actually stored ever has its Value evaluated, so exactly
                    // one FetchAsync runs per identity — no duplicate I/O, and no loser to race the finally's removal.
                    Task task = _inFlight.GetOrAdd(descriptor.Identity,
                        _ => new Lazy<Task>(() => FetchAsync(provider, descriptor, source, onChanged))).Value;
                    tasks.Add(task);
                    break;
            }
        }

        if (grew)
        {
            onChanged();
        }

        return tasks;
    }

    // The loaded catalogs across <paramref name="providers"/>, in their given order (provider precedence), for the
    // snapshot rebuild. Within a provider the catalogs are ordered by identity (ordinal), so two overlapping catalogs
    // for the same culture merge deterministically — the later one wins — regardless of the registry's (unordered)
    // internal layout or the file system's enumeration order. A failed entry (null value) is skipped.
    public IReadOnlyList<Catalog> LoadedCatalogs(IReadOnlyList<ICatalogProvider> providers)
    {
        var catalogs = new List<Catalog>();
        foreach (ICatalogProvider provider in providers)
        {
            if (_registry.TryGetValue(provider, out ConcurrentDictionary<(string Culture, string Name), Catalog?>? loaded))
            {
                foreach (KeyValuePair<(string Culture, string Name), Catalog?> entry in loaded
                    .OrderBy(pair => pair.Key.Culture, StringComparer.Ordinal)
                    .ThenBy(pair => pair.Key.Name, StringComparer.Ordinal))
                {
                    if (entry.Value is not null)
                    {
                        catalogs.Add(entry.Value);
                    }
                }
            }
        }

        return catalogs;
    }

    // Forgets one catalog so the next load re-fetches it — a hot-reload edit or an assembly-load replacement.
    public void Forget(ICatalogProvider provider, (string Culture, string Name) identity)
    {
        if (_registry.TryGetValue(provider, out ConcurrentDictionary<(string Culture, string Name), Catalog?>? loaded))
        {
            loaded.TryRemove(identity, out _);
        }
    }

    // Opens a synchronous source unless already handled, recording the catalog (or null on an expected failure).
    // Returns true only when a real catalog was newly registered. The open runs outside any lock; TryAdd re-checks so
    // a concurrent load never double-registers.
    private bool OpenSynchronous(ICatalogProvider provider, CatalogDescriptor descriptor, CatalogSource.Synchronous source)
    {
        ConcurrentDictionary<(string Culture, string Name), Catalog?> catalogs = _registry[provider];
        if (catalogs.ContainsKey(descriptor.Identity))
        {
            return false;
        }

        Catalog? catalog;
        try
        {
            catalog = source.Open();
        }
        catch (Exception exception) when (IsCatalogLoadFailure(exception))
        {
            catalog = null;
        }

        return catalogs.TryAdd(descriptor.Identity, catalog) && catalog is not null;
    }

    // Fetches an asynchronous source, records it, and signals when it grows the loaded set, then leaves the in-flight
    // queue. The only asynchronous-open site, so the miss path never opens a stream inline.
    private async Task FetchAsync(ICatalogProvider provider, CatalogDescriptor descriptor, CatalogSource.Asynchronous source, Action onChanged)
    {
        try
        {
            Catalog? catalog;
            try
            {
                catalog = await source.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsCatalogLoadFailure(exception))
            {
                catalog = null;
            }

            if (_registry[provider].TryAdd(descriptor.Identity, catalog) && catalog is not null)
            {
                onChanged();
            }
        }
        finally
        {
            _inFlight.TryRemove(descriptor.Identity, out _);
        }
    }

    // Whether an exception is an expected catalog-load failure (missing or malformed) rather than an unrelated or
    // fatal one that should propagate. OperationCanceledException is excluded, so cancellation propagates.
    private static bool IsCatalogLoadFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or HttpRequestException
            or JsonException
            or XmlException
            or FormatException
            or NotSupportedException;
}
