using System.Globalization;

[assembly: ArchPillar.Extensions.Localization.LocalizationCatalog("embedded.de.arb", "arb")]
[assembly: ArchPillar.Extensions.Localization.LocalizationSatelliteCatalogs]

namespace ArchPillar.Extensions.Localization.Tests;

/// <summary>A top-level marker type whose full name is the category of the embedded test catalog.</summary>
internal sealed class EmbeddedStrings;

/// <summary>A top-level marker type whose full name is the category of the satellite test catalog.</summary>
internal sealed class SatelliteStrings;

/// <summary>A top-level marker type used as a category in the ambient-store tests.</summary>
internal sealed class Greeting;

[Collection("Ambient")]
public sealed class LocalizationTests
{
    private static readonly CultureInfo _german = CultureInfo.GetCultureInfo("de");
    private static readonly CultureInfo _pseudo = CultureInfo.GetCultureInfo("qps-ploc");

    [Fact]
    public void AddCatalog_ResolvesThroughAmbientTypedLocalizer()
    {
        Localizer.Reset();
        Localizer.AddCatalog(DeCatalog(typeof(Greeting).FullName!, "hello", "Hallo"));

        WithCulture(_german, () => Assert.Equal("Hallo", Localizer.For<Greeting>().Translate("hello", "Hello")));
    }

    [Fact]
    public void Translate_StaticGlobal_RendersDefaultThenResolvesTheGlobalOverride()
    {
        Localizer.Reset();

        // No catalog: the static free-function form renders the in-code default through the global category.
        Assert.Equal("Hello Ada", Localizer.Translate("greeting", "Hello {name}", ("name", "Ada")));

        // A global-category (empty category) override is what the receiver-less Translate resolves against.
        Localizer.AddCatalog(DeCatalog(string.Empty, "greeting", "Hallo {name}"));
        WithCulture(_german, () => Assert.Equal("Hallo Ada", Localizer.Translate("greeting", "Hello {name}", ("name", "Ada"))));
    }

    [Fact]
    public void AddSource_LayersOverTheStore()
    {
        Localizer.Reset();
        Localizer.AddSource(new PseudoLocalizationSource("qps-ploc"));

        WithCulture(_pseudo, () => Assert.Equal("XXXXX", Localizer.For<Greeting>().Translate("hello", "Hello")));
    }

    [Fact]
    public void EmbeddedCatalog_IsDiscoveredFromTheAssembly()
    {
        Localizer.Reset();

        WithCulture(_german, () => Assert.Equal("Eingebettet", Localizer.For<EmbeddedStrings>().Translate("embedded.key", "Embedded")));
    }

    [Fact]
    public void TranslationsDirectory_LoadsCatalogsFromFilesBesideTheBinary()
    {
        var directory = Path.Combine(Path.GetTempPath(), "apldir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "de.arb"), $$"""
                {
                  "@@locale": "de",
                  "@@x-category": "{{typeof(Greeting).FullName}}",
                  "hello": "Hallo",
                  "@hello": { "x-state": "Translated", "x-source-fingerprint": "fp" }
                }
                """);

            Localizer.Reset();
            Localizer.Configure(new LocalizerOptions { TranslationsDirectory = directory });

            WithCulture(_german, () => Assert.Equal("Hallo", Localizer.For<Greeting>().Translate("hello", "Hello")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SatelliteCatalog_IsLoadedLazilyForTheRequestedCulture()
    {
        Localizer.Reset();

        WithCulture(_german, () => Assert.Equal("Aus dem Satelliten", Localizer.For<SatelliteStrings>().Translate("sat.key", "From satellite")));
    }

    [Fact]
    public void Reset_DropsHostAddedCatalogs()
    {
        Localizer.Reset();
        Localizer.AddCatalog(DeCatalog(typeof(Greeting).FullName!, "hello", "Hallo"));
        Localizer.Reset();

        WithCulture(_german, () => Assert.Equal("Hello", Localizer.For<Greeting>().Translate("hello", "Hello")));
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
