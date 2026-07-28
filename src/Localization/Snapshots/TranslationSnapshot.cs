using System.Globalization;

namespace ArchPillar.Extensions.Localization.Snapshots;

internal sealed class TranslationMap()
    : Dictionary<string, string>(StringComparer.Ordinal)
{
}

internal sealed class CategoryMap()
    : Dictionary<string, TranslationMap>(StringComparer.Ordinal)
{
}

internal sealed class CultureMap()
    : Dictionary<string, CategoryMap>(StringComparer.OrdinalIgnoreCase)
{
}

/// <summary>
/// An immutable, fully-built view of the loaded overrides. Reload builds a new snapshot in memory and
/// swaps the reference atomically, so readers never lock and never observe a half-built table. The
/// overrides are tiered by culture, then category (the full type name of the scope, empty for the global
/// namespace), then composite key, so a category-scoped lookup is a sequence of allocation-free dictionary
/// reads.
/// </summary>
internal sealed class TranslationSnapshot(
    CultureMap cultureMap)
{
    /// <summary>Maps a culture (case-insensitive) to its category-to-(composite-key-to-message) overrides.</summary>
    public CultureMap Cultures { get; } = cultureMap;

    // Resolves the override for the composite key under the category, walking from the given culture up through its
    // parent cultures — a sequence of allocation-free dictionary reads. Returns null when none is loaded; the in-code
    // default is the engine's terminal fallback, applied by the caller.
    public string? Lookup(CultureInfo culture, string category, string compositeKey)
    {
        // Hand-rolled rather than CultureChain.Of: this is the allocation-free lookup hot path, and an iterator
        // method would allocate an enumerator per call.
        for (CultureInfo? current = culture; !string.IsNullOrEmpty(current?.Name); current = current.Parent)
        {
            if (Cultures.TryGetValue(current.Name, out CategoryMap? byCategory)
                && byCategory.TryGetValue(category, out TranslationMap? map)
                && map.TryGetValue(compositeKey, out var message))
            {
                return message;
            }
        }

        return null;
    }

    // Enumerates the loaded overrides for the category in the given culture as (compositeKey, message) pairs, walking
    // from the culture up through its parent cultures when requested so a more specific culture's entry wins on
    // overlap. The IStringLocalizer adapter's GetAllStrings reads this to list the ambient entries.
    public IReadOnlyList<KeyValuePair<string, string>> EnumerateCategory(CultureInfo culture, string category, bool includeParentCultures)
    {
        var chain = new List<string>();
        foreach (CultureInfo current in CultureChain.Of(culture))
        {
            chain.Add(current.Name);
            if (!includeParentCultures)
            {
                break;
            }
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = chain.Count - 1; index >= 0; index--)
        {
            if (Cultures.TryGetValue(chain[index], out CategoryMap? byCategory)
                && byCategory.TryGetValue(category, out TranslationMap? map))
            {
                foreach (KeyValuePair<string, string> pair in map)
                {
                    result[pair.Key] = pair.Value;
                }
            }
        }

        return [.. result];
    }

    public static TranslationSnapshot Empty { get; } = new([]);

    public static TranslationSnapshot Build(
        IEnumerable<Catalog> catalogs)
    {
        CultureMap cultureMap = [];
        foreach (Catalog catalog in catalogs)
        {
            MergeEntries(catalog, cultureMap);
        }

        return new(cultureMap);
    }

    private static void MergeEntries(Catalog catalog, CultureMap byCulture)
    {
        if (!byCulture.TryGetValue(catalog.Culture, out CategoryMap? categoryMap))
        {
            categoryMap = new();
            byCulture[catalog.Culture] = categoryMap;
        }

        foreach (CatalogEntry entry in catalog.Entries)
        {
            if (entry.State is TranslationState.NeedsTranslation ||
                string.IsNullOrEmpty(entry.TranslatedMessage))
            {
                continue;
            }

            if (!categoryMap.TryGetValue(entry.Category, out TranslationMap? map))
            {
                categoryMap[entry.Category] = map = new();
            }

            map[entry.Key] = entry.TranslatedMessage!;
        }
    }
}
