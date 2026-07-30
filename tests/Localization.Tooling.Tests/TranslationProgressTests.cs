using ArchPillar.Extensions.Localization.Tooling.Internal;

namespace ArchPillar.Extensions.Localization.Tooling.Tests;

/// <summary>
/// The measurement behind <c>status</c>: a catalog against its project's template. Every detail level is an
/// aggregate of these counts, so the arithmetic here is what the whole report rests on.
/// </summary>
public sealed class TranslationProgressTests
{
    [Fact]
    public void Measure_FinalCountsAsTranslated()
    {
        // Final is reviewed and approved — more done than Translated, not less.
        Catalog catalog = MakeCatalog(
            ("a", TranslationState.Final),
            ("b", TranslationState.Translated));

        var progress = TranslationProgress.Measure(catalog, total: 2);

        Assert.Equal(2, progress.Translated);
        Assert.Equal(0, progress.Missing);
        Assert.Equal(1.0, progress.Fraction);
    }

    [Fact]
    public void Measure_NeedsReview_IsItsOwnBucketAndNotCountedAsDone()
    {
        Catalog catalog = MakeCatalog(
            ("a", TranslationState.Translated),
            ("b", TranslationState.NeedsReview));

        var progress = TranslationProgress.Measure(catalog, total: 2);

        Assert.Equal(1, progress.Translated);
        Assert.Equal(1, progress.NeedsReview);
        Assert.Equal(0, progress.Missing);
    }

    [Fact]
    public void Measure_NeedsTranslation_IsMissing()
    {
        Catalog catalog = MakeCatalog(
            ("a", TranslationState.Translated),
            ("b", TranslationState.NeedsTranslation));

        var progress = TranslationProgress.Measure(catalog, total: 2);

        Assert.Equal(1, progress.Translated);
        Assert.Equal(1, progress.Missing);
    }

    [Fact]
    public void Measure_KeysAbsentFromTheCatalog_AreMissing()
    {
        // A catalog that has not been synced since keys were added must not read as complete.
        Catalog catalog = MakeCatalog(("a", TranslationState.Translated));

        var progress = TranslationProgress.Measure(catalog, total: 4);

        Assert.Equal(4, progress.Total);
        Assert.Equal(1, progress.Translated);
        Assert.Equal(3, progress.Missing);
    }

    [Fact]
    public void Measure_CatalogWithMoreEntriesThanTheTemplate_NeverReportsNegativeMissing()
    {
        // Keys removed from code but not yet synced out of the catalog: the template is what is in scope, and the
        // counts must stay within it.
        Catalog catalog = MakeCatalog(
            ("a", TranslationState.Translated),
            ("b", TranslationState.Translated),
            ("c", TranslationState.NeedsReview));

        var progress = TranslationProgress.Measure(catalog, total: 1);

        Assert.Equal(1, progress.Total);
        Assert.Equal(1, progress.Translated);
        Assert.Equal(0, progress.NeedsReview);
        Assert.Equal(0, progress.Missing);
    }

    [Fact]
    public void Measure_EmptyTemplate_HasNoFraction()
    {
        var progress = TranslationProgress.Measure(MakeCatalog(), total: 0);

        Assert.Equal(0, progress.Total);
        Assert.Equal(0, progress.Fraction);
    }

    [Fact]
    public void Add_SumsEveryBucket()
    {
        // Aggregation to a language, a project, or the whole app is exactly this addition.
        var left = new TranslationProgress(10, 6, 2, 2);
        var right = new TranslationProgress(4, 1, 0, 3);

        TranslationProgress total = left + right;

        Assert.Equal(new TranslationProgress(14, 7, 2, 5), total);
        Assert.Equal(0.5, total.Fraction);
    }

    private static Catalog MakeCatalog(params (string Key, TranslationState State)[] entries) => new()
    {
        Culture = "de",
        Entries =
        [
            .. entries.Select(entry => new CatalogEntry
            {
                Key = entry.Key,
                SourceMessage = "src",
                TranslatedMessage = entry.State == TranslationState.NeedsTranslation ? null : "übersetzt",
                SourceFingerprint = "f",
                State = entry.State
            })
        ]
    };
}
