using System.Globalization;

namespace ArchPillar.Extensions.Localization.Tests;

public sealed class TranslationSourceTests
{
    private static readonly CultureInfo _pseudo = CultureInfo.GetCultureInfo("qps-ploc");
    private static readonly CultureInfo _english = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void Pseudo_ReplacesLettersOfTheDefault_ForItsCulture()
    {
        using var localizer = new Localizer(new LocalizerOptions
        {
            SourceCulture = "en",
            Sources = [new PseudoLocalizationSource("qps-ploc")]
        });

        Assert.Equal("XXX XXXX", localizer.Translate(_pseudo, "todo.add", "Add task", context: null));
    }

    [Fact]
    public void Pseudo_PreservesPlaceholders()
    {
        using var localizer = new Localizer(new LocalizerOptions
        {
            SourceCulture = "en",
            Sources = [new PseudoLocalizationSource("qps-ploc")]
        });

        // The placeholder survives pseudo-localization and is then filled by formatting.
        Assert.Equal("XXXXX Ada", localizer.Translate(_pseudo, "greeting", "Hello {name}", null, ("name", "Ada")));
    }

    [Fact]
    public void Pseudo_DoesNotAffectOtherCultures()
    {
        using var localizer = new Localizer(new LocalizerOptions
        {
            SourceCulture = "en",
            Sources = [new PseudoLocalizationSource("qps-ploc")]
        });

        Assert.Equal("Add task", localizer.Translate(_english, "todo.add", "Add task", context: null));
    }

    [Fact]
    public void Source_TakesPrecedenceOverCatalog()
    {
        var catalog = new Catalog
        {
            Culture = "de",
            Entries =
            [
                new CatalogEntry
                {
                    Key = "todo.add",
                    SourceMessage = "Add task",
                    TranslatedMessage = "Aufgabe hinzufügen",
                    SourceFingerprint = "fp",
                    State = TranslationState.Translated
                }
            ]
        };
        using var localizer = new Localizer(catalog, new LocalizerOptions
        {
            SourceCulture = "en",
            Sources = [new StubSource("de", "from source")]
        });

        Assert.Equal("from source", localizer.Translate(CultureInfo.GetCultureInfo("de"), "todo.add", "Add task", context: null));
    }

    private sealed class StubSource(string cultureName, string message) : ITranslationSource
    {
        public string? Resolve(CultureInfo culture, string category, string key, string defaultMessage) =>
            string.Equals(culture.Name, cultureName, StringComparison.OrdinalIgnoreCase) ? message : null;
    }
}
