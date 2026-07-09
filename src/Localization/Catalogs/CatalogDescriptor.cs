using ArchPillar.Extensions.Localization.Providers;

namespace ArchPillar.Extensions.Localization.Catalogs;

/// <summary>
/// One catalog an <see cref="ICatalogProvider"/> can supply: the culture it carries, its container format, and how
/// to open it (<see cref="CatalogSource"/>). Listing what is available never reads a catalog — the catalog is
/// produced through <see cref="Source"/> only when the store decides to load it, and the provider owns the parse, so
/// <see cref="Source"/> yields a ready <see cref="Catalog"/>.
/// </summary>
public sealed class CatalogDescriptor
{
    /// <summary>The culture tag the catalog carries (<c>de</c>, <c>de-AT</c>), or the empty string for a culture-neutral catalog.</summary>
    public required string Culture { get; init; }

    /// <summary>
    /// The container-format tag — a format id (<c>xliff</c>, <c>arb</c>, <c>po</c>) or a file extension
    /// (<c>.xliff</c>). It does <em>not</em> drive parsing (the provider bakes that into <see cref="Source"/>); a
    /// provider uses it to break ties when the same catalog exists in several formats, and for diagnostics.
    /// </summary>
    public required string Format { get; init; }

    /// <summary>An optional human-readable identifier for diagnostics — a file name, resource name, or URI.</summary>
    public string? Name { get; init; }

    /// <summary>How to open the catalog — synchronously or asynchronously (see <see cref="CatalogSource"/>).</summary>
    public required CatalogSource Source { get; init; }

    /// <summary>
    /// The (culture, name) pair identifying this catalog within a single provider's set. The loader dedupes by it,
    /// so a re-probe — or an overlap between listing and per-culture probing — loads the catalog only once.
    /// </summary>
    public (string Culture, string Name) Identity => (Culture, Name ?? string.Empty);
}
