using System.Globalization;
using System.Text;
using ArchPillar.Extensions.Localization.Formats;

namespace ArchPillar.Extensions.Localization.Tests;

/// <summary>A marker type whose full name is the category used by the configured-provider catalogs.</summary>
internal sealed class ProviderStrings;

/// <summary>
/// Proves the store is provider-agnostic: an <see cref="InMemoryCatalogProvider"/> configured through
/// <see cref="LocalizerOptions.Providers"/> loads and resolves through the same path as the built-in directory
/// provider, and — layered after the configured providers — wins on overlap.
/// </summary>
public sealed class CatalogProviderTests
{
    private static readonly CultureInfo _german = CultureInfo.GetCultureInfo("de");

    [Fact]
    public void ConfiguredProvider_CatalogsLoadAndResolve()
    {
        var provider = new InMemoryCatalogProvider([ParseArb("de", "Hallo")]);
        using var context = new LocalizationContext(new LocalizerOptions { SourceCulture = "en", Providers = [_ => provider] });

        WithCulture(_german, () => Assert.Equal("Hallo", context.For<ProviderStrings>().Translate("hello", "Hello")));
    }

    [Fact]
    public void ConfiguredProvider_WinsOverTheDirectoryProviderOnOverlap()
    {
        // A directory the auto-default reads, holding a different translation than the configured provider. The
        // configured provider is layered after the directory provider, so it wins on the last-wins merge.
        var directory = Path.Combine(Path.GetTempPath(), "aplprov-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "de.arb"), Arb("de", "Aus dem Verzeichnis"));

            var provider = new InMemoryCatalogProvider([ParseArb("de", "Vom Provider")]);
            using var context = new LocalizationContext(new LocalizerOptions { TranslationsDirectory = directory, Providers = [_ => provider] });

            WithCulture(_german, () => Assert.Equal("Vom Provider", context.For<ProviderStrings>().Translate("hello", "Hello")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Constructor_NullCatalogs_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new InMemoryCatalogProvider(null!));

    private static Catalog ParseArb(string culture, string hello)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Arb(culture, hello)));
        return new ArbTranslationFormat().Read(stream);
    }

    private static string Arb(string culture, string hello) => $$"""
        {
          "@@locale": "{{culture}}",
          "@@x-category": "{{typeof(ProviderStrings).FullName}}",
          "hello": "{{hello}}",
          "@hello": { "x-state": "Translated", "x-source-fingerprint": "fp" }
        }
        """;

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
