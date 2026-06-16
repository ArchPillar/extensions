using ArchPillar.Extensions.Localization.Formats;

namespace ArchPillar.Extensions.Localization.Internal;

/// <summary>
/// Builds a <see cref="TranslationSnapshot"/> by reading every translation file in the configured
/// directory through the bundled format providers, grouping by culture, skipping untranslated entries, and
/// resolving cross-format overlap by the configured precedence. The source language is loaded as an override
/// layer like any other culture; the in-code default remains the terminal fallback, and an un-customized
/// source entry (an echo of that default) stays <see cref="TranslationState.NeedsTranslation"/> so the
/// per-entry filter drops it and only a genuine source override is loaded.
/// </summary>
internal static class CatalogLoader
{
    public static TranslationSnapshot Load(LocalizerOptions options) =>
        BuildSnapshot(LoadDirectory(options), options);

    /// <summary>
    /// Reads every translation file in the configured directory into a list of catalogs, ordered so the
    /// lowest-precedence format comes first — a later catalog wins on overlap, so the layered merge reproduces
    /// the format precedence (<c>xliff</c> &gt; <c>arb</c> &gt; <c>po</c> by default). Filtering (source
    /// culture, untranslated entries) is left to <see cref="BuildSnapshot"/>, which the directory layer feeds.
    /// </summary>
    public static List<Catalog> LoadDirectory(LocalizerOptions options) => LoadDirectory(options, cultures: null);

    /// <summary>
    /// As <see cref="LoadDirectory(LocalizerOptions)"/>, but when <paramref name="cultures"/> is non-null only
    /// reads files whose name ends with one of those culture tags — the on-demand path, which opens just the
    /// requested cultures' files instead of the whole directory.
    /// </summary>
    public static List<Catalog> LoadDirectory(LocalizerOptions options, IReadOnlySet<string>? cultures)
    {
        var catalogs = new List<Catalog>();
        if (!Directory.Exists(options.TranslationsDirectory))
        {
            return catalogs;
        }

        TranslationFormatRegistry registry = BuildRegistry();
        foreach (var file in OrderedFiles(options.TranslationsDirectory, registry, options.FormatPrecedence))
        {
            if (cultures?.Contains(CultureFromFileName(file)) == false)
            {
                continue;
            }

            ITranslationFormat? format = registry.ResolveByExtension(Path.GetExtension(file));
            if (format is null)
            {
                continue;
            }

            Catalog? catalog = TryRead(format, file);
            if (catalog is not null)
            {
                catalogs.Add(catalog);
            }
        }

        return catalogs;
    }

    // The culture tag a catalog file name ends with: App.Web.de.xliff -> "de", de.arb -> "de". The runtime
    // resolves the authoritative culture from the file's content; this name-based read only decides which files
    // on-demand loading opens, keying off the {AssemblyName}.{culture}.{ext} naming convention.
    private static string CultureFromFileName(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var lastDot = name.LastIndexOf('.');
        return lastDot >= 0 ? name[(lastDot + 1)..] : name;
    }

    public static TranslationSnapshot BuildSnapshot(IEnumerable<Catalog> catalogs, LocalizerOptions options)
    {
        Dictionary<string, Dictionary<string, Dictionary<string, string>>> byCulture = NewCultureMap();
        foreach (Catalog catalog in catalogs)
        {
            if (!ShouldSkipCulture(catalog.Culture, options))
            {
                MergeEntries(catalog, byCulture);
            }
        }

        return ToSnapshot(byCulture);
    }

    /// <summary>
    /// Loads <paramref name="catalogs"/> exactly as the runtime does — last source wins, untranslated entries
    /// skipped, the source language included as an override layer (only its genuine overrides survive) — and
    /// dumps the merged result as one flattened <see cref="Catalog"/> per culture (the publish bundle). A
    /// culture with no surviving entries produces no bundle, so an un-customized source language ships nothing.
    /// Because it reuses <see cref="BuildSnapshot"/>, the bundle resolves identically to loading the many files.
    /// Translator-only metadata is dropped; a runtime bundle needs only the translations.
    /// </summary>
    public static IReadOnlyList<Catalog> Flatten(IEnumerable<Catalog> catalogs, LocalizerOptions options)
    {
        TranslationSnapshot snapshot = BuildSnapshot(catalogs, options);
        var result = new List<Catalog>();
        foreach (KeyValuePair<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> culture in snapshot.ByCulture)
        {
            var entries = new List<CatalogEntry>();
            foreach (KeyValuePair<string, IReadOnlyDictionary<string, string>> category in culture.Value)
            {
                foreach (KeyValuePair<string, string> entry in category.Value)
                {
                    (var key, var context) = SplitComposite(entry.Key);
                    entries.Add(new CatalogEntry
                    {
                        Category = category.Key,
                        Key = key,
                        Context = context,
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
                .ThenBy(entry => entry.Key, StringComparer.Ordinal)
                .ThenBy(entry => entry.Context ?? string.Empty, StringComparer.Ordinal);
            result.Add(new Catalog { Culture = culture.Key, Entries = [.. ordered] });
        }

        return result;
    }

    private static (string Key, string? Context) SplitComposite(string composite)
    {
        var separator = composite.IndexOf(TranslationKey.Separator);
        return separator >= 0
            ? (composite[(separator + 1)..], composite[..separator])
            : (composite, null);
    }

    private static Dictionary<string, Dictionary<string, Dictionary<string, string>>> NewCultureMap() =>
        new(StringComparer.OrdinalIgnoreCase);

    private static TranslationFormatRegistry BuildRegistry()
    {
        var registry = new TranslationFormatRegistry();
        registry.Register(new ArbTranslationFormat());
        registry.Register(new XliffTranslationFormat());
        registry.Register(new PoTranslationFormat());
        return registry;
    }

    private static IEnumerable<string> OrderedFiles(
        string directory,
        TranslationFormatRegistry registry,
        IReadOnlyList<string> precedence)
    {
        var files = new List<string>();
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (registry.ResolveByExtension(Path.GetExtension(file)) is not null)
            {
                files.Add(file);
            }
        }

        // Process lowest-priority formats first so higher-priority formats overwrite on overlap. Break a tie
        // (two files of the same format, hence equal rank) by ordinal path so the winner is deterministic
        // rather than dependent on the file system's enumeration order: the later path wins on overlap.
        files.Sort((left, right) =>
        {
            var byRank = Rank(right, registry, precedence).CompareTo(Rank(left, registry, precedence));
            return byRank != 0 ? byRank : string.CompareOrdinal(left, right);
        });
        return files;
    }

    private static int Rank(string file, TranslationFormatRegistry registry, IReadOnlyList<string> precedence)
    {
        ITranslationFormat? format = registry.ResolveByExtension(Path.GetExtension(file));
        if (format is null)
        {
            return int.MaxValue;
        }

        for (var index = 0; index < precedence.Count; index++)
        {
            if (string.Equals(precedence[index], format.FormatId, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    private static bool ShouldSkipCulture(string culture, LocalizerOptions options)
    {
        if (string.IsNullOrEmpty(culture))
        {
            return true;
        }

        // The source language loads as an override layer above the in-code default (which stays the terminal
        // fallback): only entries a human actually edited survive the per-entry filter, so an un-customized
        // source catalog contributes nothing. The source culture bypasses the Cultures allow-list, which
        // constrains which *target* languages load, not the base language.
        if (string.Equals(culture, options.SourceCulture, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return options.Cultures?.Contains(culture, StringComparer.OrdinalIgnoreCase) == false;
    }

    private static void MergeEntries(Catalog catalog, Dictionary<string, Dictionary<string, Dictionary<string, string>>> byCulture)
    {
        if (!byCulture.TryGetValue(catalog.Culture, out Dictionary<string, Dictionary<string, string>>? byCategory))
        {
            byCategory = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            byCulture[catalog.Culture] = byCategory;
        }

        foreach (CatalogEntry entry in catalog.Entries)
        {
            if (!HasTranslation(entry))
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

    private static bool HasTranslation(CatalogEntry entry) =>
        entry.State != TranslationState.NeedsTranslation && !string.IsNullOrEmpty(entry.TranslatedMessage);

    private static Catalog? TryRead(ITranslationFormat format, string file)
    {
        try
        {
            using FileStream stream = File.OpenRead(file);
            return format.ReadAsync(stream, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // A malformed file must not take down the application; skip it and load the rest.
            return null;
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
