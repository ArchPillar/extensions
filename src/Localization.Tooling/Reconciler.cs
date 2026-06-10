namespace ArchPillar.Extensions.Localization.Tooling;

/// <summary>
/// Merges a target catalog against the current source template (the gettext <c>msgmerge</c> equivalent,
/// rebuilt for the <see cref="Catalog"/> model): adds new keys, refreshes metadata, flags drift and
/// placeholder changes for review while keeping the translation, and deletes keys that no longer exist
/// in the template. <c>add &lt;lang&gt;</c> is this same merge starting from an empty target.
/// </summary>
internal static class Reconciler
{
    public static Catalog Reconcile(Catalog template, Catalog target)
    {
        Dictionary<string, CatalogEntry> existing = Index(target);
        var entries = new List<CatalogEntry>();
        foreach (CatalogEntry source in Ordered(template.Entries))
        {
            entries.Add(existing.TryGetValue(Composite(source), out CatalogEntry? current)
                ? Merge(source, current)
                : New(source));
        }

        // Keys present in the target but no longer in the template are simply dropped (Decision D-11).
        return target with { Entries = entries };
    }

    public static Catalog CreateLanguage(Catalog template, string culture) =>
        Reconcile(template, new Catalog { Culture = culture, Entries = [] });

    private static CatalogEntry New(CatalogEntry source) => new()
    {
        Key = source.Key,
        Category = source.Category,
        Context = source.Context,
        SourceMessage = source.SourceMessage,
        TranslatedMessage = null,
        Comment = source.Comment,
        References = source.References,
        Placeholders = source.Placeholders,
        SourceFingerprint = source.SourceFingerprint,
        State = TranslationState.NeedsTranslation
    };

    private static CatalogEntry Merge(CatalogEntry source, CatalogEntry current)
    {
        var sourceDrifted = !string.Equals(current.SourceFingerprint, source.SourceFingerprint, StringComparison.Ordinal);
        var placeholdersChanged = !current.Placeholders.SequenceEqual(source.Placeholders, StringComparer.Ordinal);
        var needsReview = sourceDrifted || placeholdersChanged;

        return current with
        {
            // Always refresh the displayed source and non-translation metadata from the template.
            SourceMessage = source.SourceMessage,
            References = source.References,
            Comment = source.Comment,
            Placeholders = source.Placeholders,
            SourceFingerprint = source.SourceFingerprint,
            // Record the prior source on drift so a translator can diff; never blank the translation. ARB
            // targets store one value per entry (source == translation), so there is no distinct prior source
            // to record — only do so when the two differ (PO/XLIFF, which carry both).
            PreviousSource = sourceDrifted && !string.Equals(current.SourceMessage, current.TranslatedMessage, StringComparison.Ordinal)
                ? current.SourceMessage
                : current.PreviousSource,
            // Flag for review only when a translation exists; an untranslated entry stays as-is.
            State = needsReview && current.State != TranslationState.NeedsTranslation
                ? TranslationState.NeedsReview
                : current.State
        };
    }

    private static Dictionary<string, CatalogEntry> Index(Catalog catalog)
    {
        var index = new Dictionary<string, CatalogEntry>(StringComparer.Ordinal);
        foreach (CatalogEntry entry in catalog.Entries)
        {
            index[Composite(entry)] = entry;
        }

        return index;
    }

    private static List<CatalogEntry> Ordered(IReadOnlyList<CatalogEntry> entries)
    {
        var ordered = new List<CatalogEntry>(entries);
        ordered.Sort(static (left, right) => CompareReference(left, right));
        return ordered;
    }

    private static int CompareReference(CatalogEntry left, CatalogEntry right)
    {
        SourceReference? leftReference = left.References.Count > 0 ? left.References[0] : null;
        SourceReference? rightReference = right.References.Count > 0 ? right.References[0] : null;
        var byPath = string.CompareOrdinal(leftReference?.FilePath ?? string.Empty, rightReference?.FilePath ?? string.Empty);
        if (byPath != 0)
        {
            return byPath;
        }

        var byLine = (leftReference?.Line ?? 0).CompareTo(rightReference?.Line ?? 0);
        return byLine != 0 ? byLine : string.CompareOrdinal(left.Key, right.Key);
    }

    // Identity includes the category: the same key under two categories is two distinct entries (matching
    // the template, the runtime snapshot, and the on-disk qualified member), reconciled independently.
    private static string Composite(CatalogEntry entry) =>
        entry.Category + TranslationKey.Separator + TranslationKey.Compose(entry.Key, entry.Context);
}
