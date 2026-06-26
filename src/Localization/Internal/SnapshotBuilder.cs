namespace ArchPillar.Extensions.Localization.Internal;

// Merges the loaded catalogs into the immutable translation snapshot. Providers merge in order (lowest precedence
// first); within a provider the catalogs are already format-selected at discovery, so there is nothing to sort. The
// source culture is the resolved one — an override exempt from the Cultures allow-list.
internal static class SnapshotBuilder
{
    public static TranslationSnapshot Build(
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
        return CatalogLoader.BuildSnapshot(all, snapshotOptions);
    }
}
