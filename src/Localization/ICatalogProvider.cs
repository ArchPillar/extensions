using System.Globalization;

namespace ArchPillar.Extensions.Localization;

/// <summary>
/// A source of translation catalogs. Discovery — finding what catalogs exist — happens at construction: a local
/// provider scans synchronously; the HTTP manifest provider fetches its index in a <c>static CreateAsync</c>. A
/// constructed provider is therefore "born ready" and exposes a synchronous descriptor inventory. Whether a
/// catalog's bytes open synchronously or asynchronously is carried per-descriptor by <see cref="CatalogSource"/>,
/// so the provider itself has no asynchronous members. This is the public extension point for a custom source.
/// </summary>
public interface ICatalogProvider
{
    /// <summary>
    /// Every catalog this provider can enumerate, discovered at construction. Empty when nothing can be
    /// enumerated up front — a catalog found only by probing a specific culture appears through
    /// <see cref="CatalogsFor"/>, not here.
    /// </summary>
    public IReadOnlyList<CatalogDescriptor> Catalogs { get; }

    /// <summary>
    /// The catalogs this provider has for <paramref name="culture"/> (its exact culture; the store walks the
    /// parent chain). May surface descriptors not in <see cref="Catalogs"/> — a culture satellite is found only by
    /// probing for it. Returns an empty list when it has none.
    /// </summary>
    /// <param name="culture">The culture whose catalogs to list.</param>
    public IReadOnlyList<CatalogDescriptor> CatalogsFor(CultureInfo culture);

    /// <summary>
    /// Starts watching for change — a file edited under hot reload, a satellite assembly loaded later — invoking
    /// <paramref name="onChanged"/> with the catalog that changed or newly appeared. The store calls this only
    /// when hot reload is enabled. Returns a handle that stops watching when disposed; a provider whose catalogs
    /// never change returns a no-op handle and never invokes the callback.
    /// </summary>
    /// <param name="onChanged">Invoked with the descriptor of the changed or new catalog.</param>
    public IDisposable Watch(Action<CatalogDescriptor> onChanged);
}
