using ArchPillar.Extensions.Localization.Tooling.Internal;

namespace ArchPillar.Extensions.Localization.Tooling.Tests;

public sealed class ReconcilerTests
{
    [Fact]
    public void ReconcileSource_NewKey_IsAnEchoNotAnOverride()
    {
        // A freshly extracted source key echoes the in-code default: no stored translation, NeedsTranslation,
        // so the runtime loader skips it (the in-code default is the terminal fallback).
        Catalog template = MakeCatalog("en", Entry("home", "Home", Fp("Home")));

        CatalogEntry entry = Single(Reconciler.ReconcileSource(template, MakeCatalog("en")));

        Assert.Equal(TranslationState.NeedsTranslation, entry.State);
        Assert.Null(entry.TranslatedMessage);
        Assert.Equal("Home", entry.SourceMessage);
    }

    [Fact]
    public void ReconcileSource_UnchangedEcho_TracksTheCurrentDefault()
    {
        // The on-disk value still hashes to its stored fingerprint, so it was never hand-edited — re-base it
        // onto the (drifted) current default rather than pinning the old text.
        Catalog template = MakeCatalog("en", Entry("home", "Homepage", Fp("Homepage")));
        Catalog existing = MakeCatalog("en", Entry("home", "Home", Fp("Home")));

        CatalogEntry entry = Single(Reconciler.ReconcileSource(template, existing));

        Assert.Equal(TranslationState.NeedsTranslation, entry.State);
        Assert.Null(entry.TranslatedMessage);
        Assert.Equal("Homepage", entry.SourceMessage);
        Assert.Equal(Fp("Homepage"), entry.SourceFingerprint);
    }

    [Fact]
    public void ReconcileSource_HandEditedEcho_BecomesTranslatedOverride()
    {
        // A human edited the source value of an echo (the file still held NeedsTranslation); the edit no longer
        // hashes to the stored fingerprint, so it is preserved as a genuine source override.
        Catalog template = MakeCatalog("en", Entry("home", "Home", Fp("Home")));
        Catalog existing = MakeCatalog("en", Entry("home", "Howdy", Fp("Home")));

        CatalogEntry entry = Single(Reconciler.ReconcileSource(template, existing));

        Assert.Equal(TranslationState.Translated, entry.State);
        Assert.Equal("Howdy", entry.TranslatedMessage);
    }

    [Fact]
    public void ReconcileSource_OverrideWithDriftedDefault_IsPreservedAndFlaggedForReview()
    {
        Catalog template = MakeCatalog("en", Entry("home", "Hello", Fp("Hello")));
        Catalog existing = MakeCatalog("en", Entry("home", "Howdy", Fp("Home"), "Howdy", TranslationState.Translated));

        CatalogEntry entry = Single(Reconciler.ReconcileSource(template, existing));

        Assert.Equal(TranslationState.NeedsReview, entry.State);
        Assert.Equal("Howdy", entry.TranslatedMessage);
        Assert.Equal("Hello", entry.SourceMessage);
        Assert.Equal(Fp("Hello"), entry.SourceFingerprint);
    }

    [Fact]
    public void ReconcileSource_OverrideRevertedToDefault_CollapsesBackToEcho()
    {
        // The override text now equals the current default, so it is no longer an override.
        Catalog template = MakeCatalog("en", Entry("home", "Home", Fp("Home")));
        Catalog existing = MakeCatalog("en", Entry("home", "Home", Fp("Home"), "Home", TranslationState.Translated));

        CatalogEntry entry = Single(Reconciler.ReconcileSource(template, existing));

        Assert.Equal(TranslationState.NeedsTranslation, entry.State);
        Assert.Null(entry.TranslatedMessage);
    }

    [Fact]
    public void ReconcileSource_RemovedKey_IsDropped()
    {
        Catalog template = MakeCatalog("en", Entry("a", "A", Fp("A")));
        Catalog existing = MakeCatalog(
            "en",
            Entry("a", "A", Fp("A")),
            Entry("b", "B", Fp("B"), "Bee", TranslationState.Translated));

        Assert.Equal("a", Single(Reconciler.ReconcileSource(template, existing)).Key);
    }

    [Fact]
    public void ReconcileSource_IsIdempotent()
    {
        Catalog template = MakeCatalog("en", Entry("home", "Home", Fp("Home")));
        Catalog once = Reconciler.ReconcileSource(template, MakeCatalog("en", Entry("home", "Howdy", Fp("Home"))));

        Catalog twice = Reconciler.ReconcileSource(template, once);

        CatalogEntry first = Single(once);
        CatalogEntry second = Single(twice);
        Assert.Equal(first.State, second.State);
        Assert.Equal(first.TranslatedMessage, second.TranslatedMessage);
        Assert.Equal(first.SourceFingerprint, second.SourceFingerprint);
    }

    private static string Fp(string source) => TemplateBuilder.Fingerprint(source, null);

    [Fact]
    public void Reconcile_NewKey_IsAddedAsNeedsTranslation()
    {
        Catalog template = MakeCatalog("en", Entry("home", "Home", "fp1"));

        CatalogEntry entry = Single(Reconciler.Reconcile(template, MakeCatalog("de")));

        Assert.Equal("home", entry.Key);
        Assert.Equal(TranslationState.NeedsTranslation, entry.State);
        Assert.Null(entry.TranslatedMessage);
        Assert.Equal("fp1", entry.SourceFingerprint);
    }

    [Fact]
    public void Reconcile_UnchangedSource_KeepsTranslationAndState()
    {
        Catalog template = MakeCatalog("en", Entry("home", "Home", "fp1"));
        Catalog target = MakeCatalog("de", Entry("home", "Home", "fp1", "Startseite", TranslationState.Translated));

        CatalogEntry entry = Single(Reconciler.Reconcile(template, target));

        Assert.Equal(TranslationState.Translated, entry.State);
        Assert.Equal("Startseite", entry.TranslatedMessage);
    }

    [Fact]
    public void Reconcile_DriftedSource_FlagsReviewKeepsTranslationRecordsPrevious()
    {
        Catalog template = MakeCatalog("en", Entry("home", "Home page", "fp2"));
        Catalog target = MakeCatalog("de", Entry("home", "Home", "fp1", "Startseite", TranslationState.Translated));

        CatalogEntry entry = Single(Reconciler.Reconcile(template, target));

        Assert.Equal(TranslationState.NeedsReview, entry.State);
        Assert.Equal("Startseite", entry.TranslatedMessage);
        Assert.Equal("Home page", entry.SourceMessage);
        Assert.Equal("fp2", entry.SourceFingerprint);
        Assert.Equal("Home", entry.PreviousSource);
    }

    [Fact]
    public void Reconcile_SourceWithPlaceholderChange_FlagsReviewViaFingerprint()
    {
        // A placeholder change is a source-message change, so it carries a new fingerprint — that is what
        // flags the review, not a separate placeholder comparison.
        Catalog template = MakeCatalog("en", Entry("greet", "Hi {name}", "fp2", placeholders: ["name"]));
        Catalog target = MakeCatalog("de", Entry("greet", "Hi", "fp1", "Hallo", TranslationState.Translated));

        CatalogEntry entry = Single(Reconciler.Reconcile(template, target));

        Assert.Equal(TranslationState.NeedsReview, entry.State);
        Assert.Equal("Hallo", entry.TranslatedMessage);
    }

    [Fact]
    public void Reconcile_TargetMissingPlaceholders_DoesNotReFlagWhenSourceUnchanged()
    {
        // PO/XLIFF do not persist placeholders, so a translated target reads back with none. With the source
        // unchanged (same fingerprint) this must stay Translated rather than be re-flagged on every sync.
        Catalog template = MakeCatalog("en", Entry("greet", "Hi {name}", "fp1", placeholders: ["name"]));
        Catalog target = MakeCatalog("de", Entry("greet", "Hi {name}", "fp1", "Hallo", TranslationState.Translated));

        CatalogEntry entry = Single(Reconciler.Reconcile(template, target));

        Assert.Equal(TranslationState.Translated, entry.State);
        Assert.Equal("Hallo", entry.TranslatedMessage);
    }

    [Fact]
    public void Reconcile_RemovedKey_IsDeleted()
    {
        Catalog template = MakeCatalog("en", Entry("a", "A", "fpa"));
        Catalog target = MakeCatalog(
            "de",
            Entry("a", "A", "fpa", "TA", TranslationState.Translated),
            Entry("b", "B", "fpb", "TB", TranslationState.Translated));

        Catalog result = Reconciler.Reconcile(template, target);

        Assert.Equal("a", Single(result).Key);
    }

    [Fact]
    public void CreateLanguage_MakesEveryEntryNeedsTranslation()
    {
        Catalog template = MakeCatalog("en", Entry("a", "A", "fpa"), Entry("b", "B", "fpb"));

        Catalog result = Reconciler.CreateLanguage(template, "de");

        Assert.Equal("de", result.Culture);
        Assert.Equal(2, result.Entries.Count);
        Assert.All(result.Entries, e => Assert.Equal(TranslationState.NeedsTranslation, e.State));
    }

    [Fact]
    public void Reconcile_IsIdempotent()
    {
        Catalog template = MakeCatalog("en", Entry("home", "Home", "fp1"));
        Catalog once = Reconciler.Reconcile(template, MakeCatalog("de"));

        Catalog twice = Reconciler.Reconcile(template, once);

        CatalogEntry first = Single(once);
        CatalogEntry second = Single(twice);
        Assert.Equal(first.State, second.State);
        Assert.Equal(first.TranslatedMessage, second.TranslatedMessage);
        Assert.Equal(first.SourceFingerprint, second.SourceFingerprint);
    }

    [Fact]
    public void Reconcile_ArbStyleDrift_DoesNotRecordTheTranslationAsPreviousSource()
    {
        Catalog template = MakeCatalog("en", Entry("home", "Homepage", "fp2"));
        // An ARB target stores one value per entry, so source == translation; there is no distinct prior
        // source to record on drift (formerly it wrote the translation as x-previous-source).
        Catalog target = MakeCatalog("de", Entry("home", "Startseite", "fp1", "Startseite", TranslationState.Translated));

        CatalogEntry entry = Single(Reconciler.Reconcile(template, target));

        Assert.Equal(TranslationState.NeedsReview, entry.State);
        Assert.Null(entry.PreviousSource);
    }

    [Fact]
    public void Reconcile_SameKeyInTwoCategories_ReconciledIndependentlyAndCategoryPreserved()
    {
        Catalog template = MakeCatalog(
            "en",
            Entry("save", "Save", "fp1", category: "App.Labels"),
            Entry("save", "Save", "fp1", category: "App.Buttons"));
        Catalog target = MakeCatalog(
            "de",
            Entry("save", "Save", "fp1", "Speichern", TranslationState.Translated, category: "App.Labels"));

        Catalog result = Reconciler.Reconcile(template, target);

        Assert.Equal(2, result.Entries.Count);
        CatalogEntry labels = result.Entries.Single(e => e.Category == "App.Labels");
        CatalogEntry buttons = result.Entries.Single(e => e.Category == "App.Buttons");
        // The existing translation is kept under its category; the other category is a new, untranslated entry
        // whose category is carried through (it was dropped before, leaving it unreachable at runtime).
        Assert.Equal("Speichern", labels.TranslatedMessage);
        Assert.Null(buttons.TranslatedMessage);
        Assert.Equal(TranslationState.NeedsTranslation, buttons.State);
        Assert.Equal("App.Buttons", buttons.Category);
    }

    [Fact]
    public void Reconcile_TranslatorComment_IsPreservedWhileDeveloperCommentRefreshes()
    {
        Catalog template = MakeCatalog("en", Entry("home", "Home", "fp1") with { Comment = "new dev note" });
        Catalog target = MakeCatalog(
            "de",
            Entry("home", "Home", "fp1", "Startseite", TranslationState.Translated) with
            {
                Comment = "old dev note",
                TranslatorComment = "translator's own note"
            });

        CatalogEntry entry = Single(Reconciler.Reconcile(template, target));

        Assert.Equal("translator's own note", entry.TranslatorComment);
        Assert.Equal("new dev note", entry.Comment);
    }

    private static CatalogEntry Entry(
        string key,
        string source,
        string fingerprint,
        string? translated = null,
        TranslationState state = TranslationState.NeedsTranslation,
        IReadOnlyList<string>? placeholders = null,
        string category = "") => new()
    {
        Key = key,
        Category = category,
        SourceMessage = source,
        SourceFingerprint = fingerprint,
        TranslatedMessage = translated,
        State = state,
        Placeholders = placeholders ?? []
    };

    private static Catalog MakeCatalog(string culture, params CatalogEntry[] entries) =>
        new() { Culture = culture, Entries = entries };

    private static CatalogEntry Single(Catalog catalog) => Assert.Single(catalog.Entries);
}
