using ArchPillar.Extensions.Localization.Providers;

namespace ArchPillar.Extensions.Localization.Snapshots;

/// <summary>
/// An immutable, fully-built view of the loaded overrides. Reload builds a new snapshot in memory and
/// swaps the reference atomically, so readers never lock and never observe a half-built table. The
/// overrides are tiered by culture, then category (the full type name of the scope, empty for the global
/// namespace), then composite key, so a category-scoped lookup is a sequence of allocation-free dictionary
/// reads.
/// </summary>
internal sealed class TranslationSnapshot
{
    public TranslationSnapshot(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> byCulture)
    {
        ByCulture = byCulture;
    }

    /// <summary>Maps a culture (case-insensitive) to its category-to-(composite-key-to-message) overrides.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> ByCulture { get; }

    public static TranslationSnapshot Empty { get; } =
        new(new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>(StringComparer.OrdinalIgnoreCase));

    public static TranslationSnapshot Build(
        IEnumerable<ProviderState> providers)
    {
        return Build(providers.SelectMany(state => state.Catalogs.Values));
    }

    public static TranslationSnapshot Build(
        IEnumerable<Catalog> catalogs)
    {
        Dictionary<string, Dictionary<string, Dictionary<string, string>>> cultureMap = NewCultureMap();
        foreach (Catalog catalog in catalogs)
        {
            MergeEntries(catalog, cultureMap);
        }

        return ToSnapshot(cultureMap);
    }

    private static Dictionary<string, Dictionary<string, Dictionary<string, string>>> NewCultureMap() =>
        new(StringComparer.OrdinalIgnoreCase);

    private static void MergeEntries(Catalog catalog, Dictionary<string, Dictionary<string, Dictionary<string, string>>> byCulture)
    {
        if (!byCulture.TryGetValue(catalog.Culture, out Dictionary<string, Dictionary<string, string>>? byCategory))
        {
            byCategory = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            byCulture[catalog.Culture] = byCategory;
        }

        foreach (CatalogEntry entry in catalog.Entries)
        {
            if (entry.State is TranslationState.NeedsTranslation ||
                string.IsNullOrEmpty(entry.TranslatedMessage))
            {
                continue;
            }

            if (!byCategory.TryGetValue(entry.Category, out Dictionary<string, string>? map))
            {
                map = new Dictionary<string, string>(StringComparer.Ordinal);
                byCategory[entry.Category] = map;
            }

            map[TranslationKey.Compose(entry.Key, entry.Context)] = entry.TranslatedMessage!;
        }
    }

    private static TranslationSnapshot ToSnapshot(Dictionary<string, Dictionary<string, Dictionary<string, string>>> byCulture)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, Dictionary<string, Dictionary<string, string>>> culture in byCulture)
        {
            var byCategory = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, Dictionary<string, string>> category in culture.Value)
            {
                byCategory[category.Key] = category.Value;
            }

            result[culture.Key] = byCategory;
        }

        return new TranslationSnapshot(result);
    }
}
