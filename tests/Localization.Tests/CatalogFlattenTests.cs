using ArchPillar.Extensions.Localization.Internal;

namespace ArchPillar.Extensions.Localization.Tests;

public sealed class CatalogFlattenTests
{
    [Fact]
    public void Flatten_OnePerCulture_LastWins_IncludesSourceOverrides_SkipsUntranslated()
    {
        Catalog[] catalogs =
        [
            Cat("de", Translated("save", "App.A", "Speichern-A")),
            Cat("de", Translated("save", "App.A", "Speichern-B")),
            Cat("de", Untranslated("only", "App.A")),
            Cat("fr", Translated("save", "App.A", "Sauvegarder")),
            Cat("en", Translated("save", "App.A", "Save")),    // a genuine source override
            Cat("en", Untranslated("echo", "App.A")),          // an echo of the default → dropped
        ];

        IReadOnlyList<Catalog> merged = CatalogLoader.Flatten(catalogs, new LocalizerOptions { SourceCulture = "en" });

        // The source language is now an override layer: en is included (carrying its translated override),
        // alongside de + fr.
        Assert.Equal(3, merged.Count);

        Catalog en = merged.Single(c => c.Culture == "en");
        CatalogEntry enEntry = Assert.Single(en.Entries); // the "echo" untranslated entry was dropped
        Assert.Equal("save", enEntry.Key);
        Assert.Equal("Save", enEntry.TranslatedMessage);

        Catalog de = merged.Single(c => c.Culture == "de");
        CatalogEntry entry = Assert.Single(de.Entries); // "only" was untranslated → skipped
        Assert.Equal("save", entry.Key);
        Assert.Equal("App.A", entry.Category);
        Assert.Equal("Speichern-B", entry.TranslatedMessage); // last catalog won
    }

    private static Catalog Cat(string culture, CatalogEntry entry) =>
        new() { Culture = culture, Entries = [entry] };

    private static CatalogEntry Translated(string key, string category, string message) => new()
    {
        Key = key,
        Category = category,
        SourceMessage = message,
        TranslatedMessage = message,
        SourceFingerprint = "fp",
        State = TranslationState.Translated
    };

    private static CatalogEntry Untranslated(string key, string category) => new()
    {
        Key = key,
        Category = category,
        SourceMessage = "x",
        TranslatedMessage = null,
        SourceFingerprint = "fp",
        State = TranslationState.NeedsTranslation
    };
}
