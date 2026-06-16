using System.Globalization;

namespace ArchPillar.Extensions.Localization.Tests;

/// <summary>
/// Eager versus on-demand culture loading: eager reads every culture up front (the server default), while
/// on-demand reads a culture's files only when it is first requested and switches language live — no restart.
/// </summary>
public sealed class CultureLoadingTests
{
    private static readonly CultureInfo _german = CultureInfo.GetCultureInfo("de");
    private static readonly CultureInfo _french = CultureInfo.GetCultureInfo("fr");
    private static readonly string _category = typeof(Greeting).FullName!;

    [Fact]
    public void Eager_LoadsEveryCultureUpFront()
    {
        var directory = NewDirectory();
        try
        {
            WriteCatalog(directory, "de", "Hallo");
            WriteCatalog(directory, "fr", "Bonjour");

            using var store = new CatalogStore(new LocalizerOptions { TranslationsDirectory = directory });

            // The default eager mode reads both cultures without anyone requesting them.
            Assert.Contains("de", store.Snapshot.ByCulture.Keys);
            Assert.Contains("fr", store.Snapshot.ByCulture.Keys);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void OnDemand_ReadsACulturesFilesOnlyWhenItIsFirstRequested()
    {
        var directory = NewDirectory();
        try
        {
            WriteCatalog(directory, "de", "Hallo");
            WriteCatalog(directory, "fr", "Bonjour");

            using var store = new CatalogStore(new LocalizerOptions
            {
                TranslationsDirectory = directory,
                CultureLoading = CultureLoading.OnDemand
            });

            // Nothing loaded up front.
            Assert.Empty(store.Snapshot.ByCulture);

            store.EnsureCulture(_german);
            Assert.Contains("de", store.Snapshot.ByCulture.Keys);
            Assert.DoesNotContain("fr", store.Snapshot.ByCulture.Keys); // not read until requested

            store.EnsureCulture(_french);
            Assert.Contains("fr", store.Snapshot.ByCulture.Keys);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void OnDemand_NativeApi_SwitchesLanguageLiveWithoutReload()
    {
        var directory = NewDirectory();
        try
        {
            WriteCatalog(directory, "de", "Hallo");
            WriteCatalog(directory, "fr", "Bonjour");

            using var context = new LocalizationContext(new LocalizerOptions
            {
                TranslationsDirectory = directory,
                CultureLoading = CultureLoading.OnDemand
            });
            ILocalizer<Greeting> localizer = context.For<Greeting>();

            // Each switch resolves the new language by loading it on the fly — same context, no restart.
            Assert.Equal("Hallo", WithCulture(_german, () => localizer.Translate("hello", "Hello")));
            Assert.Equal("Bonjour", WithCulture(_french, () => localizer.Translate("hello", "Hello")));
            Assert.Equal("Hallo", WithCulture(_german, () => localizer.Translate("hello", "Hello"))); // already loaded
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void OnDemand_AnUntranslatedCulture_FallsToTheInCodeDefault()
    {
        var directory = NewDirectory();
        try
        {
            WriteCatalog(directory, "de", "Hallo");

            using var context = new LocalizationContext(new LocalizerOptions
            {
                TranslationsDirectory = directory,
                CultureLoading = CultureLoading.OnDemand
            });
            ILocalizer<Greeting> localizer = context.For<Greeting>();

            // No 'fr' file: requesting it loads nothing and renders the in-code default — never a crash.
            Assert.Equal("Hello", WithCulture(_french, () => localizer.Translate("hello", "Hello")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "apl-cultureload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void WriteCatalog(string directory, string culture, string message) =>
        File.WriteAllText(Path.Combine(directory, $"App.{culture}.arb"), $$"""
            {
              "@@locale": "{{culture}}",
              "@@x-category": "{{_category}}",
              "hello": "{{message}}",
              "@hello": { "x-state": "Translated", "x-source-fingerprint": "fp" }
            }
            """);

    private static T WithCulture<T>(CultureInfo culture, Func<T> action)
    {
        CultureInfo original = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = culture;
        try
        {
            return action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
