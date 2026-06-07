using ArchPillar.Extensions.Localization.Internal;

namespace ArchPillar.Extensions.Localization.Tests;

public sealed class CatalogFlattenTests
{
    [Fact]
    public void Flatten_OnePerCulture_LastWins_SkipsSourceAndUntranslated()
    {
        Catalog[] catalogs =
        [
            Cat("de", Translated("save", "App.A", "Speichern-A")),
            Cat("de", Translated("save", "App.A", "Speichern-B")),
            Cat("de", Untranslated("only", "App.A")),
            Cat("fr", Translated("save", "App.A", "Sauvegarder")),
            Cat("en", Translated("save", "App.A", "Save")),
        ];

        IReadOnlyList<Catalog> merged = CatalogLoader.Flatten(catalogs, new LocalizerOptions { SourceCulture = "en" });

        // en (source) is skipped; de + fr remain.
        Assert.Equal(2, merged.Count);
        Assert.DoesNotContain(merged, c => c.Culture == "en");

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
