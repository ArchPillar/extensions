using ArchPillar.Extensions.Localization.Tooling.Internal;

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

    /// <summary>
    /// Merges the freshly extracted template into the existing source-language catalog instead of overwriting
    /// it, so the source catalog is a stable, git-tracked artifact whose hand edits survive a re-extract. An
    /// entry that merely echoes the in-code default stays <see cref="TranslationState.NeedsTranslation"/> (it
    /// is not a runtime override — the in-code default is the terminal fallback) and silently tracks the
    /// current default; an entry whose on-disk source text was edited away from that default is preserved as a
    /// <see cref="TranslationState.Translated"/> override (flagged <see cref="TranslationState.NeedsReview"/>
    /// when the in-code default has drifted under it). Keys gone from the template are dropped (Decision D-11).
    /// </summary>
    public static Catalog ReconcileSource(Catalog template, Catalog existingSource)
    {
        Dictionary<string, CatalogEntry> existing = Index(existingSource);
        var entries = new List<CatalogEntry>();
        foreach (CatalogEntry source in Ordered(template.Entries))
        {
            entries.Add(existing.TryGetValue(Composite(source), out CatalogEntry? current)
                ? MergeSource(source, current)
                : Echo(source));
        }

        return existingSource with { Culture = template.Culture, Entries = entries };
    }

    // A source entry that equals the in-code default: no stored translation and NeedsTranslation, so the
    // runtime loader drops it (the default already covers it) and it tracks the current default each extract.
    private static CatalogEntry Echo(CatalogEntry source) => source with
    {
        TranslatedMessage = null,
        PreviousSource = null,
        State = TranslationState.NeedsTranslation
    };

    private static CatalogEntry MergeSource(CatalogEntry source, CatalogEntry existing)
    {
        // The editable source text on disk: a provider that stores one value per entry round-trips it into
        // both fields; one that stores source and translation separately keeps the edit in TranslatedMessage.
        var onDisk = existing.TranslatedMessage ?? existing.SourceMessage;

        // The stored fingerprint is of the default this entry was last written against. If the on-disk value
        // still hashes to it, the value is an un-customized echo (possibly of an older default) — re-base it
        // onto the current default. If it differs, a human edited the source wording, so keep it as an override.
        var customized = !string.Equals(
            TemplateBuilder.Fingerprint(onDisk, source.Context),
            existing.SourceFingerprint,
            StringComparison.Ordinal);
        if (!customized)
        {
            return Echo(source);
        }

        // A genuine source override. Flag it for review when the in-code default drifted since the edit was
        // recorded; a fresh edit over a previously un-translated echo becomes translated; otherwise the
        // existing review-or-translated state is kept. Refresh the displayed source and the non-translation
        // metadata from the template, exactly as the target merge does.
        var driftedSinceEdit = !string.Equals(existing.SourceFingerprint, source.SourceFingerprint, StringComparison.Ordinal);
        TranslationState state = ResolveOverrideState(existing.State, driftedSinceEdit);
        return existing with
        {
            SourceMessage = source.SourceMessage,
            TranslatedMessage = onDisk,
            Comment = source.Comment,
            References = source.References,
            Placeholders = source.Placeholders,
            SourceFingerprint = source.SourceFingerprint,
            State = state
        };
    }

    private static TranslationState ResolveOverrideState(TranslationState existing, bool driftedSinceEdit)
    {
        if (driftedSinceEdit)
        {
            return TranslationState.NeedsReview;
        }

        return existing == TranslationState.NeedsTranslation ? TranslationState.Translated : existing;
    }

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
        // The fingerprint is computed from the source message (and context), and the placeholder set is
        // derived from that same source message, so any placeholder change is already a fingerprint change.
        // Comparing placeholders separately is redundant — and actively harmful for formats that do not
        // persist them (PO, XLIFF), whose targets read back with no placeholders and would otherwise be
        // re-flagged for review on every sync. Drift is therefore decided by the fingerprint alone.
        var sourceDrifted = !string.Equals(current.SourceFingerprint, source.SourceFingerprint, StringComparison.Ordinal);
        var needsReview = sourceDrifted;

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
