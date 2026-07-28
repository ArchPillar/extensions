using ArchPillar.Extensions.Localization.Snapshots;

namespace ArchPillar.Extensions.Localization.Catalogs;

/// <summary>
/// Builds a <see cref="TranslationSnapshot"/> by reading every translation file in the configured
/// directory through the bundled format providers, grouping by culture, skipping untranslated entries, and
/// resolving cross-format overlap by the configured precedence. The source language is loaded as an override
/// layer like any other culture; the in-code default remains the terminal fallback, and an un-customized
/// source entry (an echo of that default) stays <see cref="TranslationState.NeedsTranslation"/> so the
/// per-entry filter drops it and only a genuine source override is loaded.
/// </summary>
internal static class CatalogFlattener
{
    /// <summary>
    /// Loads <paramref name="catalogs"/> exactly as the runtime does — last source wins, untranslated entries
    /// skipped, the source language included as an override layer (only its genuine overrides survive) — and
    /// dumps the merged result as one flattened <see cref="Catalog"/> per culture (the publish bundle). A
    /// culture with no surviving entries produces no bundle, so an un-customized source language ships nothing.
    /// Because it reuses <see cref="TranslationSnapshot"/>, the bundle resolves identically to loading the many files.
    /// Translator-only metadata is dropped; a runtime bundle needs only the translations.
    /// </summary>
    public static IReadOnlyList<Catalog> Flatten(IEnumerable<Catalog> catalogs)
    {
        var snapshot = TranslationSnapshot.Build(catalogs);
        var result = new List<Catalog>();
        foreach (KeyValuePair<string, CategoryMap> culture in snapshot.Cultures)
        {
            var entries = new List<CatalogEntry>();
            foreach (KeyValuePair<string, TranslationMap> category in culture.Value)
            {
                foreach (KeyValuePair<string, string> entry in category.Value)
                {
                    entries.Add(new CatalogEntry
                    {
                        Category = category.Key,
                        Key = entry.Key,
                        SourceMessage = entry.Value,
                        TranslatedMessage = entry.Value,
                        SourceFingerprint = string.Empty,
                        State = TranslationState.Translated
                    });
                }
            }

            // A culture that contributed no loadable entry (e.g. a source catalog of pure echoes) yields no
            // bundle rather than an empty file.
            if (entries.Count == 0)
            {
                continue;
            }

            IEnumerable<CatalogEntry> ordered = entries
                .OrderBy(entry => entry.Category, StringComparer.Ordinal)
                .ThenBy(entry => entry.Key, StringComparer.Ordinal);
            result.Add(new Catalog { Culture = culture.Key, Entries = [.. ordered] });
        }

        return result;
    }
}
