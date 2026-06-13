namespace ArchPillar.Extensions.Localization;

/// <summary>
/// The format-neutral, in-memory representation of a translation file: a culture plus an ordered set
/// of <see cref="CatalogEntry"/>. The reconciler and the runtime operate only on this model; each
/// format provider serializes to and from it.
/// </summary>
public sealed record Catalog
{
    /// <summary>The BCP-47 culture. The source-locale catalog uses the source language.</summary>
    public required string Culture { get; init; }

    /// <summary>The catalog entries, in a deterministic order.</summary>
    public required IReadOnlyList<CatalogEntry> Entries { get; init; }

    /// <summary>Format-specific header values, round-tripped opaquely.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>();
}
