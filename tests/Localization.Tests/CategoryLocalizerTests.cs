using System.Globalization;

namespace ArchPillar.Extensions.Localization.Tests;

public sealed class CategoryLocalizerTests
{
    private static readonly CultureInfo _german = CultureInfo.GetCultureInfo("de");

    [Fact]
    public void TypedLocalizer_ResolvesWithinItsOwnCategory()
    {
        using var root = Localizer.FromCatalogs(
            [DeCatalog(("save", typeof(Save).FullName!, "Speichern"), ("save", typeof(Cancel).FullName!, "Abbrechen"))],
            new LocalizerOptions { SourceCulture = "en" });
        var factory = new LocalizerFactory(root);
        ILocalizer<Save> save = factory.Create<Save>();
        ILocalizer<Cancel> cancel = factory.Create<Cancel>();

        WithCulture(_german, () =>
        {
            // Same key "save" under two categories resolves to each category's own translation.
            Assert.Equal("Speichern", save.Translate("save", "Save"));
            Assert.Equal("Abbrechen", cancel.Translate("save", "Save"));
        });
    }

    [Fact]
    public void TypedLocalizer_MissInCategory_FallsThroughToInCodeDefault()
    {
        using var root = Localizer.FromCatalogs(
            [DeCatalog(("save", typeof(Save).FullName!, "Speichern"))],
            new LocalizerOptions { SourceCulture = "en" });
        var factory = new LocalizerFactory(root);

        // "save" is not categorized under Cancel, so the in-code default wins.
        WithCulture(_german, () => Assert.Equal("Save", factory.Create<Cancel>().Translate("save", "Save")));
    }

    [Fact]
    public void GlobalLocalizer_DoesNotSeeCategorizedOverrides()
    {
        using var root = Localizer.FromCatalogs(
            [DeCatalog(("save", typeof(Save).FullName!, "Speichern"))],
            new LocalizerOptions { SourceCulture = "en" });

        // The bare ILocalizer looks up the global (empty) category, so a categorized override is invisible.
        WithCulture(_german, () => Assert.Equal("Save", ((ILocalizer)root).Translate("save", "Save")));
    }

    [Fact]
    public void Factory_CachesTypedLocalizerPerType()
    {
        using var root = Localizer.FromCatalogs([], new LocalizerOptions { SourceCulture = "en" });
        var factory = new LocalizerFactory(root);

        Assert.Same(factory.Create<Save>(), factory.Create<Save>());
    }

    private static Catalog DeCatalog(params (string Key, string Category, string Message)[] entries) => new()
    {
        Culture = "de",
        Entries = [.. entries.Select(e => new CatalogEntry
        {
            Category = e.Category,
            Key = e.Key,
            SourceMessage = "Save",
            TranslatedMessage = e.Message,
            SourceFingerprint = "fp",
            State = TranslationState.Translated
        })]
    };

    private static void WithCulture(CultureInfo culture, Action action)
    {
        CultureInfo original = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = culture;
        try
        {
            action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    private sealed class Save;

    private sealed class Cancel;
}
