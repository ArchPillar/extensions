using System.Globalization;
using ArchPillar.Extensions.Localization.Providers;

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

    [Fact]
    public void ConfiguredCatalog_ResolvesThroughAmbientTypedLocalizer()
    {
        Localizer.Ambient.Reset();
        Localizer.Ambient.Configure(new LocalizerOptions { Providers = [Layer(DeCatalog(typeof(Greeting).FullName!, "hello", "Hallo"))] });

        WithCulture(_german, () => Assert.Equal("Hallo", Localizer.For<Greeting>().Translate("hello", "Hello")));
    }

    [Fact]
    public void Translate_StaticGlobal_RendersDefaultThenResolvesTheGlobalOverride()
    {
        Localizer.Ambient.Reset();

        // No catalog: the static free-function form renders the in-code default through the global category.
        Assert.Equal("Hello Ada", Localizer.Translate("greeting", "Hello {name}", ("name", "Ada")));

        // A global-category (empty category) override is what the receiver-less Translate resolves against.
        Localizer.Ambient.Configure(new LocalizerOptions { Providers = [Layer(DeCatalog(string.Empty, "greeting", "Hallo {name}"))] });
        WithCulture(_german, () => Assert.Equal("Hallo Ada", Localizer.Translate("greeting", "Hello {name}", ("name", "Ada"))));
    }

    [Fact]
    public void EmbeddedCatalog_IsDiscoveredFromTheAssembly()
    {
        Localizer.Ambient.Reset();

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

            Localizer.Ambient.Reset();
            Localizer.Ambient.Configure(new LocalizerOptions { TranslationsDirectory = directory });

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
        Localizer.Ambient.Reset();

        WithCulture(_german, () => Assert.Equal("Aus dem Satelliten", Localizer.For<SatelliteStrings>().Translate("sat.key", "From satellite")));
    }

    [Fact]
    public void Reset_DropsConfiguredCatalogs()
    {
        Localizer.Ambient.Reset();
        Localizer.Ambient.Configure(new LocalizerOptions { Providers = [Layer(DeCatalog(typeof(Greeting).FullName!, "hello", "Hallo"))] });
        Localizer.Ambient.Reset();

        WithCulture(_german, () => Assert.Equal("Hello", Localizer.For<Greeting>().Translate("hello", "Hello")));
    }

    [Fact]
    public void Initialize_CalledAgainWithEqualOptions_DoesNotReconfigureTheAmbient()
    {
        Localizer.ResetAmbientForTests();
        // Two distinct options instances sharing one provider factory: value-equal, so the guard dedupes them even
        // though they are not the same object.
        Func<LocalizerOptions, ICatalogProvider> factory = Layer(DeCatalog(typeof(Greeting).FullName!, "hello", "Hallo"));
        Localizer.Initialize(new LocalizerOptions { Providers = [factory] });
        WithCulture(_german, () => Assert.Equal("Hallo", Localizer.For<Greeting>().Translate("hello", "Hello")));

        // A repeat call with an equal configuration is a no-op: it must not rebuild the ambient, which would raise
        // CatalogsChanged. The subscription is added after the first configure, so only a redundant rebuild trips it.
        var raised = 0;
        Localizer.CatalogsChanged += () => raised++;
        Localizer.Initialize(new LocalizerOptions { Providers = [factory] });

        Assert.Equal(0, raised);
        WithCulture(_german, () => Assert.Equal("Hallo", Localizer.For<Greeting>().Translate("hello", "Hello")));

        Localizer.ResetAmbientForTests();
    }

    [Fact]
    public void Reset_ThenInitializeWithEqualOptions_ReconfiguresAgainstTheEmptiedStore()
    {
        Localizer.ResetAmbientForTests();
        var options = new LocalizerOptions { Providers = [Layer(DeCatalog(typeof(Greeting).FullName!, "hello", "Hallo"))] };
        Localizer.Initialize(options);
        WithCulture(_german, () => Assert.Equal("Hallo", Localizer.For<Greeting>().Translate("hello", "Hello")));

        // Resetting the ambient empties the store; a later Initialize with the very same options must re-apply, not
        // skip on a stale "already configured" memo. The configuration is owned by the context and Reset returns it
        // to the default, so the dedupe cannot leave the store empty here.
        Localizer.Ambient.Reset();
        Localizer.Initialize(options);

        WithCulture(_german, () => Assert.Equal("Hallo", Localizer.For<Greeting>().Translate("hello", "Hello")));

        Localizer.ResetAmbientForTests();
    }

    [Fact]
    public void Initialize_CalledAgainWithDifferentOptions_ReconfiguresTheAmbient()
    {
        Localizer.ResetAmbientForTests();
        Localizer.Initialize(new LocalizerOptions { Providers = [Layer(DeCatalog(typeof(Greeting).FullName!, "hello", "Hallo"))] });
        WithCulture(_german, () => Assert.Equal("Hallo", Localizer.For<Greeting>().Translate("hello", "Hello")));

        // Different options apply, so the override changes — configure-once dedupes equal configurations only.
        Localizer.Initialize(new LocalizerOptions { Providers = [Layer(DeCatalog(typeof(Greeting).FullName!, "hello", "Servus"))] });
        WithCulture(_german, () => Assert.Equal("Servus", Localizer.For<Greeting>().Translate("hello", "Hello")));

        Localizer.ResetAmbientForTests();
    }

    // Serves a fixed catalog through an InMemoryCatalogProvider, so a test can layer it through
    // LocalizerOptions.Providers the way a host configures any catalog provider.
    private static Func<LocalizerOptions, ICatalogProvider> Layer(Catalog catalog) =>
        _ => new InMemoryCatalogProvider([catalog]);

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
