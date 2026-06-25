namespace ArchPillar.Extensions.Localization.Internal;

// A committed catalog with the format it was parsed from, so the snapshot builder can order by precedence.
internal sealed record LoadedCatalog(Catalog Catalog, string Format);

// One provider's bookkeeping: committed catalogs (deduped by identity), failed identities (dropped, not retried),
// and the watch handle. Mutated only under the catalog store's gate.
internal sealed class ProviderState(ICatalogProvider provider)
{
    public ICatalogProvider Provider { get; } = provider;

    public Dictionary<(string Culture, string Name), LoadedCatalog> Catalogs { get; } = [];

    public HashSet<(string Culture, string Name)> Failed { get; } = [];

    public IDisposable? Watch { get; set; }
}
