namespace ArchPillar.Extensions.Localization.Tooling.Internal;

/// <summary>
/// How far one slice of the work is translated: the number of strings that are done, that carry a translation the
/// source has drifted under, and that have none at all. A slice is a project/language pair, or any aggregate of
/// them (a whole language, a whole project, the whole app) — the counts add up, which is what lets one collected
/// set of pairs answer every level of detail.
/// </summary>
/// <param name="Total">The strings in scope for this slice.</param>
/// <param name="Translated">Strings carrying a current translation (<c>Translated</c> or <c>Final</c>).</param>
/// <param name="NeedsReview">Strings whose translation exists but whose source drifted under it.</param>
/// <param name="Missing">Strings with no translation at all — untranslated, or absent from the catalog.</param>
internal readonly record struct TranslationProgress(int Total, int Translated, int NeedsReview, int Missing)
{
    /// <summary>The share of <see cref="Total"/> that is done, 0–1; zero when there is nothing in scope.</summary>
    public double Fraction => Total == 0 ? 0 : (double)Translated / Total;

    /// <summary>Adds two slices — the operation every aggregate level is built from.</summary>
    public static TranslationProgress operator +(TranslationProgress left, TranslationProgress right) =>
        new(
            left.Total + right.Total,
            left.Translated + right.Translated,
            left.NeedsReview + right.NeedsReview,
            left.Missing + right.Missing);

    /// <summary>The named form of <see cref="op_Addition"/>, for callers that cannot use the operator.</summary>
    public static TranslationProgress Add(TranslationProgress left, TranslationProgress right) => left + right;

    /// <summary>
    /// The progress of <paramref name="catalog"/> against a template of <paramref name="total"/> strings. A key the
    /// template has and the catalog does not is <see cref="Missing"/>, so a stale catalog never flatters the count;
    /// entries the catalog carries beyond the template (a key removed from code but not yet synced) are ignored,
    /// since the template is what is in scope.
    /// </summary>
    public static TranslationProgress Measure(Catalog catalog, int total)
    {
        var translated = 0;
        var needsReview = 0;
        foreach (CatalogEntry entry in catalog.Entries)
        {
            switch (entry.State)
            {
                // Final is reviewed and approved — as done as Translated, and counted with it.
                case TranslationState.Translated or TranslationState.Final:
                    translated++;
                    break;
                case TranslationState.NeedsReview:
                    needsReview++;
                    break;
                default:
                    break;
            }
        }

        // Clamp to the template: a catalog holding more entries than the template must not push the accounted
        // count past the total and make Missing negative.
        translated = Math.Min(translated, total);
        needsReview = Math.Min(needsReview, total - translated);
        return new TranslationProgress(total, translated, needsReview, total - translated - needsReview);
    }
}

/// <summary>One project/language pair's progress — the grain everything else aggregates from.</summary>
/// <param name="Project">The assembly the strings belong to.</param>
/// <param name="Culture">The target language.</param>
/// <param name="Progress">That pair's counts.</param>
internal sealed record TranslationProgressRow(string Project, string Culture, TranslationProgress Progress);

/// <summary>How much of the status report to show: the whole app, per language, per project, or every pair.</summary>
internal enum StatusDetail
{
    /// <summary>One line for the whole app, across every project and language.</summary>
    Overall,

    /// <summary>One row per target language, across every project.</summary>
    Language,

    /// <summary>One row per project, across every language.</summary>
    Project,

    /// <summary>One row per project/language pair — the full matrix.</summary>
    Matrix
}
