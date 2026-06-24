namespace ArchPillar.Extensions.Localization;

/// <summary>
/// One catalog an <see cref="ICatalogProvider"/> can supply: the culture it carries, its container format, and
/// how to open its bytes (<see cref="CatalogSource"/>). Listing what is available never reads a catalog — the bytes
/// are opened through <see cref="Source"/> only when the store decides to load it, and the store parses the
/// returned stream with the matching format provider.
/// </summary>
public sealed class CatalogDescriptor
{
    /// <summary>The culture tag the catalog carries (<c>de</c>, <c>de-AT</c>), or the empty string for a culture-neutral catalog.</summary>
    public required string Culture { get; init; }

    /// <summary>
    /// The container-format hint the store uses to pick a parser: a format id (<c>xliff</c>, <c>arb</c>,
    /// <c>po</c>) or a file extension (<c>.xliff</c>). Matched case-insensitively against the registered formats.
    /// </summary>
    public required string Format { get; init; }

    /// <summary>An optional human-readable identifier for diagnostics — a file name, resource name, or URI.</summary>
    public string? Name { get; init; }

    /// <summary>How to open the catalog's bytes — synchronously or asynchronously (see <see cref="CatalogSource"/>).</summary>
    public required CatalogSource Source { get; init; }

    /// <summary>
    /// The (culture, name) pair identifying this catalog within a single provider's set. The store dedupes by
    /// it, so a re-probe — or an overlap between listing and per-culture probing — loads the catalog only once.
    /// </summary>
    public (string Culture, string Name) Identity => (Culture, Name ?? string.Empty);
}
