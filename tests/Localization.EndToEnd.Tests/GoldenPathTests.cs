using System.Globalization;
using System.Text;
using ArchPillar.Extensions.Localization.Formats;
using ArchPillar.Extensions.Localization.Tooling;
using Ambient = ArchPillar.Extensions.Localization.Localizer;

namespace ArchPillar.Extensions.Localization.EndToEnd.Tests;

/// <summary>
/// The whole library in one story, driving the real generator, the real reconciler, and the real runtime —
/// no mocks. This is the repository's standing argument that the advertised workflow actually works and stays
/// easy: a developer writes a string and ships an in-code default, the build extracts it, a translator fills
/// one file, and the app renders the translation per culture. If any link in that chain regresses, this test
/// goes red.
/// </summary>
public sealed class GoldenPathTests : IDisposable
{
    private static readonly CultureInfo _german = CultureInfo.GetCultureInfo("de");
    private static readonly CultureInfo _english = CultureInfo.GetCultureInfo("en");

    private readonly string _translationsDirectory;

    public GoldenPathTests()
    {
        _translationsDirectory = Path.Combine(Path.GetTempPath(), "apl-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_translationsDirectory);
        Ambient.Reset();
    }

    [Fact]
    public async Task DeveloperWritesAString_BuildExtracts_TranslatorFills_AppRendersAsync()
    {
        // 1. The developer writes ordinary code: a translatable call site with an in-code English default.
        //    No catalog, no config, no key declared by hand — the literal is both the key and the default.
        const string DeveloperCode = """
            using ArchPillar.Extensions.Localization;

            public static class T
            {
                public static string Translate([Translatable] string key, [TranslationDefault] string message) => message;
            }

            public sealed class HomePage
            {
                public string Title() => T.Translate("home.title", "Home");
            }
            """;

        // 2. The build runs the generator, which extracts the source strings into a template (here decoded
        //    from the assembly attribute the generator bakes — exactly what the tool reads at sync time).
        Catalog template = await ReadArbAsync(GeneratorPipeline.ExtractTemplateArb(DeveloperCode));
        CatalogEntry source = Assert.Single(template.Entries);
        Assert.Equal("home.title", source.Key);
        Assert.Equal("Home", source.SourceMessage);
        Assert.NotEmpty(source.SourceFingerprint);

        // 3. The tool hands the translator a German file: every entry present and flagged NeedsTranslation.
        Catalog forTranslator = Reconciler.CreateLanguage(template, "de");
        Assert.All(forTranslator.Entries, e => Assert.Equal(TranslationState.NeedsTranslation, e.State));

        // 4. The translator does the one human step — fills in the German and marks it done — and hands the
        //    file back. We persist it to the app's Translations directory as it would arrive in source control.
        Catalog handedBack = new()
        {
            Culture = "de",
            Entries = [.. forTranslator.Entries.Select(e => e with
            {
                TranslatedMessage = "Startseite",
                State = TranslationState.Translated
            })]
        };
        await WriteArbAsync(Path.Combine(_translationsDirectory, "de.arb"), handedBack);

        // 5. The app loads translations from that directory and renders per request culture — with zero
        //    call-site changes from step 1.
        Ambient.SourceCulture = "en";
        Ambient.TranslationsDirectory = _translationsDirectory;
        ILocalizer localizer = Ambient.Default;

        Assert.Equal("Startseite", WithCulture(_german, () => localizer.Translate("home.title", "Home")));

        // The source language renders the in-code default (no override needed, and none shipped).
        Assert.Equal("Home", WithCulture(_english, () => localizer.Translate("home.title", "Home")));

        // A key with no entry anywhere still renders its in-code default — the terminal fallback never fails.
        Assert.Equal("Settings", WithCulture(_german, () => localizer.Translate("home.settings", "Settings")));
    }

    [Fact]
    public async Task IcuPluralWithArgument_SurvivesExtraction_AndRendersPerCultureRulesAsync()
    {
        // The marquee feature, end to end: a developer ships an ICU plural as the in-code default, the
        // translator supplies the German plural, and the runtime selects the right branch by the *German*
        // CLDR rules — through the same extract → reconcile → load chain, with the argument applied.
        const string DeveloperCode = """
            using ArchPillar.Extensions.Localization;

            public static class T
            {
                public static string Translate([Translatable] string key, [TranslationDefault] string message) => message;
            }

            public sealed class Inbox
            {
                public string Summary() =>
                    T.Translate("inbox.count", "{count, plural, one {# message} other {# messages}}");
            }
            """;

        Catalog template = await ReadArbAsync(GeneratorPipeline.ExtractTemplateArb(DeveloperCode));

        Catalog handedBack = new()
        {
            Culture = "de",
            Entries = [.. Reconciler.CreateLanguage(template, "de").Entries.Select(e => e with
            {
                TranslatedMessage = "{count, plural, one {# Nachricht} other {# Nachrichten}}",
                State = TranslationState.Translated
            })]
        };
        await WriteArbAsync(Path.Combine(_translationsDirectory, "de.arb"), handedBack);

        Ambient.SourceCulture = "en";
        Ambient.TranslationsDirectory = _translationsDirectory;
        ILocalizer localizer = Ambient.Default;

        const string Default = "{count, plural, one {# message} other {# messages}}";
        Assert.Equal("1 Nachricht", WithCulture(_german, () => localizer.Translate("inbox.count", Default, ("count", 1))));
        Assert.Equal("5 Nachrichten", WithCulture(_german, () => localizer.Translate("inbox.count", Default, ("count", 5))));

        // English has no override, so the in-code default renders under English plural rules.
        Assert.Equal("1 message", WithCulture(_english, () => localizer.Translate("inbox.count", Default, ("count", 1))));
        Assert.Equal("5 messages", WithCulture(_english, () => localizer.Translate("inbox.count", Default, ("count", 5))));
    }

    public void Dispose()
    {
        Ambient.Reset();
        Directory.Delete(_translationsDirectory, recursive: true);
    }

    private static async Task<Catalog> ReadArbAsync(string arb)
    {
        var format = new ArbTranslationFormat();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(arb));
        return await format.ReadAsync(stream, CancellationToken.None);
    }

    private static async Task WriteArbAsync(string path, Catalog catalog)
    {
        var format = new ArbTranslationFormat();
        using FileStream stream = File.Create(path);
        await format.WriteAsync(stream, catalog, CancellationToken.None);
    }

    private static T WithCulture<T>(CultureInfo culture, Func<T> render)
    {
        CultureInfo original = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = culture;
        try
        {
            return render();
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
