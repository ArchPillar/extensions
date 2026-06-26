namespace ArchPillar.Extensions.Localization.Internal;

// Builds the merged translation snapshot and the resolution layers from the loaded catalogs. Providers merge in
// order (lowest precedence first); within a provider the catalogs are already format-selected at discovery, so
// there is nothing to sort. Custom sources layer newest-wins above the snapshot (itself the lowest layer); the
// source culture is the resolved one — an override exempt from the Cultures allow-list.
internal static class SnapshotBuilder
{
    public static (TranslationSnapshot Snapshot, IReadOnlyList<ITranslationSource> Layers) Build(
        IReadOnlyList<ProviderState> providers,
        LocalizerOptions options,
        string sourceCulture)
    {
        var all = new List<Catalog>();
        foreach (ProviderState provider in providers)
        {
            all.AddRange(provider.Catalogs.Values);
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
}
