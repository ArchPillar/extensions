using ArchPillar.Extensions.Localization.Formats;
using ArchPillar.Extensions.Localization.Tooling;

namespace ArchPillar.Extensions.Localization.EndToEnd.Tests;

/// <summary>
/// Dogfoods the dotnet tool a consumer is told to wire into CI, driving the real <c>add</c>, <c>sync
/// --check</c>, and <c>convert</c> commands. It proves the gate is honest in both directions — it passes when
/// the catalog is in sync and fails when a developer adds a string without re-syncing — and that a translator
/// can move a file between ARB, XLIFF, and PO without losing the content they care about.
/// </summary>
public sealed class ToolGateTests : IDisposable
{
    private const string OneString = """
        using ArchPillar.Extensions.Localization;

        public static class T
        {
            public static string Translate([Translatable] string key, [TranslationDefault] string message) => message;
        }

        public sealed class Page
        {
            public string Title() => T.Translate("home.title", "Home");
        }
        """;

    private const string TwoStrings = """
        using ArchPillar.Extensions.Localization;

        public static class T
        {
            public static string Translate([Translatable] string key, [TranslationDefault] string message) => message;
        }

        public sealed class Page
        {
            public string Title() => T.Translate("home.title", "Home");
            public string Subtitle() => T.Translate("home.subtitle", "Welcome");
        }
        """;

    private readonly string _directory;

    public ToolGateTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "apl-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public async Task SyncCheck_PassesWhenTheTargetIsInSyncAsync()
    {
        var template = Path.Combine(_directory, "en.arb");
        File.WriteAllText(template, GeneratorPipeline.ExtractTemplateArb(OneString));

        // The tool creates the German file from the template, then the gate confirms it is in sync.
        Assert.Equal(0, await ToolApplication.RunAsync(["add", "de", "--template", template, "--output", _directory]));
        Assert.Equal(0, await ToolApplication.RunAsync(["sync", "--template", template, "--target", Path.Combine(_directory, "de.arb"), "--check"]));
    }

    [Fact]
    public async Task SyncCheck_FailsWhenADeveloperAddsAStringWithoutSyncingAsync()
    {
        var template = Path.Combine(_directory, "en.arb");
        var target = Path.Combine(_directory, "de.arb");
        File.WriteAllText(template, GeneratorPipeline.ExtractTemplateArb(OneString));
        Assert.Equal(0, await ToolApplication.RunAsync(["add", "de", "--template", template, "--output", _directory]));

        // The developer adds a second string and rebuilds: the template now has a key the committed German
        // file lacks. The gate must report drift (exit 1) so CI blocks the merge until someone runs sync.
        File.WriteAllText(template, GeneratorPipeline.ExtractTemplateArb(TwoStrings));

        Assert.Equal(1, await ToolApplication.RunAsync(["sync", "--template", template, "--target", target, "--check"]));

        // And after syncing, the gate goes green again — the drift was actionable, not a dead end.
        Assert.Equal(0, await ToolApplication.RunAsync(["sync", "--template", template, "--target", target]));
        Assert.Equal(0, await ToolApplication.RunAsync(["sync", "--template", template, "--target", target, "--check"]));
    }

    [Theory]
    [InlineData("xliff", ".xlf")]
    [InlineData("po", ".po")]
    public async Task Convert_RoundTripsTranslatableContentThroughAnotherFormatAsync(string format, string extension)
    {
        // A translator's file: one finished German translation.
        var arb = Path.Combine(_directory, "de.arb");
        await WriteCatalogAsync(arb, new Catalog
        {
            Culture = "de",
            Entries =
            [
                new CatalogEntry
                {
                    Key = "greeting",
                    SourceMessage = "Hello",
                    TranslatedMessage = "Hallo",
                    State = TranslationState.Translated,
                    SourceFingerprint = "fp"
                }
            ]
        });

        // ARB -> other format -> ARB, all through the real convert command.
        var other = Path.Combine(_directory, "de" + extension);
        var roundTripped = Path.Combine(_directory, "de.roundtrip.arb");
        Assert.Equal(0, await ToolApplication.RunAsync(["convert", "--from", arb, "--to", format, "--output", other]));
        Assert.Equal(0, await ToolApplication.RunAsync(["convert", "--from", other, "--to", "arb", "--output", roundTripped]));

        // The translator's content — the key and the finished translation — survives the round trip. (A
        // single-value ARB for a target locale carries only the translation, not a separate source string,
        // so "Hallo" is the value that must come back intact.)
        CatalogEntry entry = Assert.Single(ReadCatalog(roundTripped).Entries);
        Assert.Equal("greeting", entry.Key);
        Assert.Equal("Hallo", entry.TranslatedMessage);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static async Task WriteCatalogAsync(string path, Catalog catalog)
    {
        var format = new ArbTranslationFormat();
        using FileStream stream = File.Create(path);
        await format.WriteAsync(stream, catalog, CancellationToken.None);
    }

    private static Catalog ReadCatalog(string path)
    {
        var format = new ArbTranslationFormat();
        using var stream = new MemoryStream(File.ReadAllBytes(path));
        return format.Read(stream);
    }
}
