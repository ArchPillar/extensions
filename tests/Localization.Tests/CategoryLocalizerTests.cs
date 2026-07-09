using System.Globalization;
using ArchPillar.Extensions.Localization.Providers;

namespace ArchPillar.Extensions.Localization.Tests;

public sealed class CategoryLocalizerTests : IDisposable
{
    private static readonly CultureInfo _german = CultureInfo.GetCultureInfo("de");
    private readonly List<LocalizationContext> _contexts = [];

    [Fact]
    public void TypedLocalizer_ResolvesWithinItsOwnCategory()
    {
        LocalizationContext context = Over(
            DeCatalog(("save", typeof(Save).FullName!, "Speichern"), ("save", typeof(Cancel).FullName!, "Abbrechen")));
        ILocalizer<Save> save = context.For<Save>();
        ILocalizer<Cancel> cancel = context.For<Cancel>();

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
        LocalizationContext context = Over(DeCatalog(("save", typeof(Save).FullName!, "Speichern")));

        // "save" is not categorized under Cancel, so the in-code default wins.
        WithCulture(_german, () => Assert.Equal("Save", context.For<Cancel>().Translate("save", "Save")));
    }

    [Fact]
    public void GlobalLocalizer_DoesNotSeeCategorizedOverrides()
    {
        LocalizationContext context = Over(DeCatalog(("save", typeof(Save).FullName!, "Speichern")));

        // The bare ILocalizer looks up the global (empty) category, so a categorized override is invisible.
        WithCulture(_german, () => Assert.Equal("Save", context.Default.Translate("save", "Save")));
    }

    [Fact]
    public void TypedLocalizer_GenericScopeType_ResolvesUnderTheOpenGenericCategory()
    {
        // The extractor files a generic scope type under its open-generic name (Box`1); the runtime must look
        // it up under the same name, not typeof(T).FullName, which includes the assembly-qualified type args.
        var openGeneric = typeof(Box<int>).GetGenericTypeDefinition().FullName!;
        LocalizationContext context = Over(DeCatalog(("save", openGeneric, "Speichern")));

        WithCulture(_german, () =>
            Assert.Equal("Speichern", context.For<Box<int>>().Translate("save", "Save")));
    }

    // Builds an isolated context over the given in-memory catalogs — the same path a host takes with an
    // InMemoryCatalogProvider configured through LocalizerOptions.Providers. The empty directory keeps the
    // auto-wired directory provider from contributing; the context is tracked and disposed with the fixture.
    private LocalizationContext Over(params Catalog[] catalogs)
    {
        var context = new LocalizationContext(new LocalizerOptions
        {
            TranslationsDirectory = Path.Combine(Path.GetTempPath(), "apl-empty-" + Guid.NewGuid().ToString("N")),
            SourceCulture = "en",
            Providers = [_ => new InMemoryCatalogProvider(catalogs)]
        });
        _contexts.Add(context);
        return context;
    }

    public void Dispose()
    {
        foreach (LocalizationContext context in _contexts)
        {
            context.Dispose();
        }
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

    private sealed class Box<T>;
}
