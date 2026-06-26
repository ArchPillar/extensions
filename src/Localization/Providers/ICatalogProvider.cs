using System.Globalization;
using ArchPillar.Extensions.Localization.Catalogs;

namespace ArchPillar.Extensions.Localization.Providers;

/// <summary>
/// A source of translation catalogs, discovered once at construction. Finding what catalogs exist is inherently
/// asynchronous for most sources (a filesystem scan, an HTTP index fetch), so it happens up front — a local
/// provider scans in its constructor; the HTTP manifest provider fetches its index in a <c>static CreateAsync</c> —
/// leaving a "born ready" provider with a fixed, synchronous inventory (<see cref="Catalogs"/>). A provider never
/// re-discovers on demand; the only way its catalogs change or appear after construction is a <see cref="Watch"/>
/// notification. Whether a catalog's bytes open synchronously or asynchronously is carried per-descriptor by
/// <see cref="CatalogSource"/>, so the provider has no asynchronous members. This is the public extension point for
/// a custom source.
/// </summary>
public interface ICatalogProvider
{
    /// <summary>
    /// The provider's complete catalog inventory, discovered at construction. This is the whole set;
    /// <see cref="CatalogsFor"/> only ever returns subsets of it. May be empty.
    /// </summary>
    public IReadOnlyList<CatalogDescriptor> Catalogs { get; }

    /// <summary>
    /// The subset of <see cref="Catalogs"/> for <paramref name="culture"/> (its exact culture; the store walks the
    /// parent chain itself). A pure synchronous filter: it is called on the translation-lookup miss path, so it must
    /// not discover or perform I/O, and it never returns a descriptor absent from <see cref="Catalogs"/>. Returns an
    /// empty list when the provider has nothing for the culture.
    /// </summary>
    /// <param name="culture">The culture whose catalogs to list.</param>
    public IReadOnlyList<CatalogDescriptor> CatalogsFor(CultureInfo culture);

    /// <summary>
    /// Starts watching for change — the only channel by which a provider's catalogs may change after construction
    /// (a file edited under hot reload, or a new descriptor appearing such as a satellite assembly loading). Invokes
    /// <paramref name="onChanged"/> with the changed or new descriptor, which the store then reloads on its own. The
    /// store calls this only when hot reload is enabled. Returns a handle that stops watching when disposed; a
    /// provider whose catalogs never change returns a no-op handle and never invokes the callback.
    /// </summary>
    /// <param name="onChanged">Invoked with the descriptor of the changed or new catalog.</param>
    public IDisposable Watch(Action<CatalogDescriptor> onChanged);
}
