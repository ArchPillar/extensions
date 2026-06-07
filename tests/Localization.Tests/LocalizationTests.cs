using System.Globalization;

[assembly: ArchPillar.Extensions.Localization.LocalizationCatalog("embedded.de.arb", "arb")]

namespace ArchPillar.Extensions.Localization.Tests;

/// <summary>A top-level marker type whose full name is the category of the embedded test catalog.</summary>
internal sealed class EmbeddedStrings;

/// <summary>A top-level marker type used as a category in the ambient-store tests.</summary>
internal sealed class Greeting;

public sealed class LocalizationTests
{
    private static readonly CultureInfo _german = CultureInfo.GetCultureInfo("de");
    private static readonly CultureInfo _pseudo = CultureInfo.GetCultureInfo("qps-ploc");

    [Fact]
    public void AddCatalog_ResolvesThroughAmbientTypedLocalizer()
    {
        Localization.Reset();
        Localization.AddCatalog(DeCatalog(typeof(Greeting).FullName!, "hello", "Hallo"));

        WithCulture(_german, () => Assert.Equal("Hallo", Localization.For<Greeting>().Translate("hello", "Hello")));
    }

    [Fact]
    public void AddSource_LayersOverTheStore()
    {
        Localization.Reset();
        Localization.AddSource(new PseudoLocalizationSource("qps-ploc"));

        WithCulture(_pseudo, () => Assert.Equal("XXXXX", Localization.For<Greeting>().Translate("hello", "Hello")));
    }

    [Fact]
    public void EmbeddedCatalog_IsDiscoveredFromTheAssembly()
    {
        Localization.Reset();

        WithCulture(_german, () => Assert.Equal("Eingebettet", Localization.For<EmbeddedStrings>().Translate("embedded.key", "Embedded")));
    }

    [Fact]
    public void Reset_DropsHostAddedCatalogs()
    {
        Localization.Reset();
        Localization.AddCatalog(DeCatalog(typeof(Greeting).FullName!, "hello", "Hallo"));
        Localization.Reset();

        WithCulture(_german, () => Assert.Equal("Hello", Localization.For<Greeting>().Translate("hello", "Hello")));
    }

    private static Catalog DeCatalog(string category, string key, string message) => new()
    {
        Culture = "de",
        Entries =
        [
            new CatalogEntry
            {
                Category = category,
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
