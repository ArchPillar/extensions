using System.Globalization;

namespace ArchPillar.Extensions.Localization.Tests;

/// <summary>
/// The instantiable environment: a <see cref="LocalizationContext"/> resolves on its own, with no ambient
/// static state, and two contexts never see each other's catalogs — the property that makes them safe for
/// test isolation and multi-context hosting.
/// </summary>
public sealed class LocalizationContextTests
{
    private static readonly CultureInfo _german = CultureInfo.GetCultureInfo("de");

    [Fact]
    public void Isolated_ResolvesWithoutTheAmbient()
    {
        using var context = new LocalizationContext(new LocalizerOptions { SourceCulture = "en" });
        context.AddCatalog(DeCatalog("greeting", "Hallo"));

        WithCulture(_german, () =>
        {
            Assert.Equal("Hallo", context.Default.Translate("greeting", "Hello"));
            Assert.Equal("Hallo", context.Translate("greeting", "Hello"));
        });
    }

    [Fact]
    public void TwoContexts_DoNotShareState()
    {
        using var a = new LocalizationContext(new LocalizerOptions { SourceCulture = "en" });
        using var b = new LocalizationContext(new LocalizerOptions { SourceCulture = "en" });
        a.AddCatalog(DeCatalog("greeting", "Hallo"));

        WithCulture(_german, () =>
        {
            Assert.Equal("Hallo", a.Default.Translate("greeting", "Hello"));
            Assert.Equal("Hello", b.Default.Translate("greeting", "Hello"));
        });
    }

    private static Catalog DeCatalog(string key, string message) => new()
    {
        Culture = "de",
        Entries =
        [
            new CatalogEntry
            {
                Key = key,
                SourceMessage = "Hello",
                TranslatedMessage = message,
                SourceFingerprint = "fp",
                State = TranslationState.Translated
            }
        ]
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
}
