namespace ArchPillar.Extensions.Localization.Internal;

// Builds the merged translation snapshot and the resolution layers from the loaded catalogs. Stateless beyond the
// format registry: given the providers (lowest-precedence first), the active options, and the resolved source
// culture, it orders, merges, and layers — leaving the store to own only the commit/publish bookkeeping. Merge is
// by provider order, then format precedence within a provider (xliff > arb > po).
internal sealed class SnapshotBuilder(TranslationFormatRegistry registry)
{
    private readonly TranslationFormatRegistry _registry = registry;

    // The merged snapshot plus the resolution layers: custom sources newest-wins above the snapshot, which is the
    // lowest layer. The source culture is the resolved one — an override exempt from the Cultures allow-list.
    public (TranslationSnapshot Snapshot, IReadOnlyList<ITranslationSource> Layers) Build(
        IReadOnlyList<ProviderState> providers,
        LocalizerOptions options,
        string sourceCulture)
    {
        var all = new List<Catalog>();
        foreach (ProviderState provider in providers)
        {
            all.AddRange(Ordered(provider, options.FormatPrecedence));
        }

        var snapshotOptions = new LocalizerOptions
        {
            SourceCulture = sourceCulture,
            Cultures = options.Cultures
        };
        TranslationSnapshot snapshot = CatalogLoader.BuildSnapshot(all, snapshotOptions);

        IReadOnlyList<ITranslationSource> sources = options.Sources;
        var layers = new List<ITranslationSource>(sources.Count + 1);
        for (var index = sources.Count - 1; index >= 0; index--)
        {
            layers.Add(sources[index]);
        }

        layers.Add(new SnapshotTranslationSource(snapshot));
        return (snapshot, layers);
    }

    // One provider's catalogs ordered lowest-precedence-format first, ties broken by ordinal name, so the last-wins
    // merge is deterministic regardless of dictionary (file-system) enumeration order.
    private List<Catalog> Ordered(ProviderState provider, IReadOnlyList<string> formatPrecedence)
    {
        var entries = new List<KeyValuePair<(string Culture, string Name), LoadedCatalog>>(provider.Catalogs);
        entries.Sort((left, right) =>
        {
            var byRank = Rank(right.Value.Format, formatPrecedence).CompareTo(Rank(left.Value.Format, formatPrecedence));
            return byRank != 0 ? byRank : string.CompareOrdinal(left.Key.Name, right.Key.Name);
        });

        var catalogs = new List<Catalog>(entries.Count);
        foreach (KeyValuePair<(string Culture, string Name), LoadedCatalog> entry in entries)
        {
            catalogs.Add(entry.Value.Catalog);
        }

        return catalogs;
    }

    // A format's precedence rank (lower wins once Ordered places it later); unranked sorts last.
    private int Rank(string format, IReadOnlyList<string> formatPrecedence)
    {
        ITranslationFormat? resolved = _registry.Resolve(format);
        if (resolved is null)
        {
            return int.MaxValue;
        }

        for (var index = 0; index < formatPrecedence.Count; index++)
        {
            if (string.Equals(formatPrecedence[index], resolved.FormatId, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return int.MaxValue;
    }
}
