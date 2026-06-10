namespace ArchPillar.Extensions.Localization.Tooling.Tests;

public sealed class ReconcilerTests
{
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
    public void Reconcile_PlaceholderChange_FlagsReview()
    {
        Catalog template = MakeCatalog("en", Entry("greet", "Hi {name}", "fp1", placeholders: ["name"]));
        Catalog target = MakeCatalog("de", Entry("greet", "Hi", "fp1", "Hallo", TranslationState.Translated));

        CatalogEntry entry = Single(Reconciler.Reconcile(template, target));

        Assert.Equal(TranslationState.NeedsReview, entry.State);
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
