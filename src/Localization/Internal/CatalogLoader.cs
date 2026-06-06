using ArchPillar.Extensions.Localization.Formats;

namespace ArchPillar.Extensions.Localization.Internal;

/// <summary>
/// Builds a <see cref="TranslationSnapshot"/> by reading every translation file in the configured
/// directory through the bundled format providers, grouping by culture, skipping the source language
/// and untranslated entries, and resolving cross-format overlap by the configured precedence.
/// </summary>
internal static class CatalogLoader
{
    public static TranslationSnapshot Load(LocalizerOptions options)
    {
        if (!Directory.Exists(options.TranslationsDirectory))
        {
            return TranslationSnapshot.Empty;
        }

        TranslationFormatRegistry registry = BuildRegistry();
        var byCulture = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in OrderedFiles(options.TranslationsDirectory, registry, options.FormatPrecedence))
        {
            LoadFile(file, registry, options, byCulture);
        }

        return ToSnapshot(byCulture);
    }

    private static TranslationFormatRegistry BuildRegistry()
    {
        var registry = new TranslationFormatRegistry();
        registry.Register(new ArbTranslationFormat());
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

        // Process lowest-priority formats first so higher-priority formats overwrite on overlap.
        files.Sort((left, right) => Rank(right, registry, precedence).CompareTo(Rank(left, registry, precedence)));
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

    private static void LoadFile(
        string file,
        TranslationFormatRegistry registry,
        LocalizerOptions options,
        Dictionary<string, Dictionary<string, string>> byCulture)
    {
        ITranslationFormat? format = registry.ResolveByExtension(Path.GetExtension(file));
        if (format is null)
        {
            return;
        }

        Catalog? catalog = TryRead(format, file);
        if (catalog is null || ShouldSkipCulture(catalog.Culture, options))
        {
            return;
        }

        MergeEntries(catalog, byCulture);
    }

    private static bool ShouldSkipCulture(string culture, LocalizerOptions options)
    {
        if (string.IsNullOrEmpty(culture) || string.Equals(culture, options.SourceCulture, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return options.Cultures?.Contains(culture, StringComparer.OrdinalIgnoreCase) == false;
    }

    private static void MergeEntries(Catalog catalog, Dictionary<string, Dictionary<string, string>> byCulture)
    {
        if (!byCulture.TryGetValue(catalog.Culture, out Dictionary<string, string>? map))
        {
            map = new Dictionary<string, string>(StringComparer.Ordinal);
            byCulture[catalog.Culture] = map;
        }

        foreach (CatalogEntry entry in catalog.Entries)
        {
            if (HasTranslation(entry))
            {
                map[TranslationKey.Compose(entry.Key, entry.Context)] = entry.TranslatedMessage!;
            }
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

    private static TranslationSnapshot ToSnapshot(Dictionary<string, Dictionary<string, string>> byCulture)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, Dictionary<string, string>> pair in byCulture)
        {
            result[pair.Key] = pair.Value;
        }

        return new TranslationSnapshot(result);
    }
}
