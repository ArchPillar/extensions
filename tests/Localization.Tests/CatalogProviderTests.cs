using System.Globalization;
using System.Text;

namespace ArchPillar.Extensions.Localization.Tests;

/// <summary>A marker type whose full name is the category used by the custom-provider catalogs.</summary>
internal sealed class ProviderStrings;

/// <summary>
/// Proves the store is provider-agnostic: a custom <see cref="ICatalogProvider"/> registered through
/// <see cref="LocalizationContext.AddProvider"/> loads and resolves through the same path as the built-in
/// directory provider, and — appended after the configured providers — wins on overlap.
/// </summary>
public sealed class CatalogProviderTests
{
    private static readonly CultureInfo _german = CultureInfo.GetCultureInfo("de");

    [Fact]
    public void AddedProvider_CatalogsLoadAndResolve()
    {
        var provider = new InMemoryCatalogProvider(("de", "Hallo"));
        using var context = new LocalizationContext(new LocalizerOptions { SourceCulture = "en" });
        context.AddProvider(provider);

        WithCulture(_german, () => Assert.Equal("Hallo", context.For<ProviderStrings>().Translate("hello", "Hello")));
    }

    [Fact]
    public void AddedProvider_WinsOverTheDirectoryProviderOnOverlap()
    {
        // A directory the auto-default reads, holding a different translation than the custom provider. The
        // added provider is appended after the directory provider, so it wins on the layered last-wins merge.
        var directory = Path.Combine(Path.GetTempPath(), "aplprov-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "de.arb"), Arb("de", "Aus dem Verzeichnis"));

            var provider = new InMemoryCatalogProvider(("de", "Vom Provider"));
            using var context = new LocalizationContext(new LocalizerOptions { TranslationsDirectory = directory });
            context.AddProvider(provider);

            WithCulture(_german, () => Assert.Equal("Vom Provider", context.For<ProviderStrings>().Translate("hello", "Hello")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AddedProvider_SurvivesAReconfigure()
    {
        var provider = new InMemoryCatalogProvider(("de", "Vom Provider"));
        using var context = new LocalizationContext(new LocalizerOptions { SourceCulture = "en" });
        context.AddProvider(provider);

        WithCulture(_german, () => Assert.Equal("Vom Provider", context.For<ProviderStrings>().Translate("hello", "Hello")));

        // A reconfigure rebuilds the options-derived providers but keeps the runtime-added ones, so the custom
        // provider's catalogs remain after re-applying options.
        context.Configure(new LocalizerOptions { SourceCulture = "en" });

        WithCulture(_german, () => Assert.Equal("Vom Provider", context.For<ProviderStrings>().Translate("hello", "Hello")));
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

    // A custom catalog provider that serves ARB catalogs it holds in memory — the store knows nothing of where
    // its bytes come from, so this exercises the provider-agnostic load path end to end. Its descriptors carry a
    // synchronous load, so the store loads them inline.
    private sealed class InMemoryCatalogProvider : ICatalogProvider
    {
        public InMemoryCatalogProvider(params (string Culture, string Hello)[] catalogs)
        {
            Catalogs = [.. catalogs.Select(Describe)];
        }

        public IReadOnlyList<CatalogDescriptor> Catalogs { get; }

        public IReadOnlyList<CatalogDescriptor> CatalogsFor(CultureInfo culture) =>
        [
            .. Catalogs.Where(descriptor => string.Equals(descriptor.Culture, culture.Name, StringComparison.OrdinalIgnoreCase))
        ];

        public IDisposable Watch(Action<CatalogDescriptor> onChanged) => NoOpWatch.Instance;

        private CatalogDescriptor Describe((string Culture, string Hello) catalog) => new()
        {
            Culture = catalog.Culture,
            Format = "arb",
            Name = catalog.Culture + ".arb",
            Source = new CatalogSource.Synchronous(() => new MemoryStream(Encoding.UTF8.GetBytes(Arb(catalog.Culture, catalog.Hello))))
        };

        private sealed class NoOpWatch : IDisposable
        {
            public static readonly NoOpWatch Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
