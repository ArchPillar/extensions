using System.IO.Compression;
using ArchPillar.Extensions.Localization.Formats;

namespace ArchPillar.Extensions.Localization.Tooling.Tests;

/// <summary>
/// Spectre reads <c>[</c> as the start of a style tag, so every runtime value the tool renders — an assembly name,
/// a culture, a zip entry from a translator — is data that must be escaped rather than parsed. These pin that
/// boundary: a name carrying markup characters is shown literally instead of taking the command down.
/// </summary>
[Collection(ToolInvocationCollection.Name)]
public sealed class ConsoleMarkupTests : IDisposable
{
    private static readonly ArbTranslationFormat _arb = new();

    private readonly string _directory;

    public ConsoleMarkupTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "aplmarkup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("extract")]
    [InlineData("add")]
    [InlineData("sync")]
    [InlineData("convert")]
    [InlineData("export")]
    [InlineData("import")]
    [InlineData("merge")]
    [InlineData("manifest")]
    public async Task Help_ForEveryCommand_RendersInsteadOfBeingParsedAsMarkupAsync(string command)
    {
        // One option description containing a literal "[...]" takes down the whole page — every option of every
        // command that inherits it becomes unreadable — so --help is the cheapest guard over all of them.
        Assert.Equal(0, await ToolApplication.RunAsync([command, "--help"]));
    }

    [Fact]
    public async Task Import_ZipEntryNameWithBrackets_ImportsInsteadOfCrashingAsync()
    {
        // A re-downloaded kit arrives with "[1]" in the entry name. This is external input, and the name is
        // rendered before the catalog is even parsed, so nothing else can reject it first.
        var kit = Path.Combine(_directory, "kit.zip");
        using (ZipArchive archive = ZipFile.Open(kit, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = archive.CreateEntry("App.de[1].arb");
            using Stream stream = entry.Open();
            await stream.WriteAsync(await SerializeCatalogAsync("de", ("save", "Speichern")));
        }

        var imported = Path.Combine(_directory, "imported");

        Assert.Equal(0, await ToolApplication.RunAsync(["import", "--input", kit, "--output", imported]));
        Assert.True(File.Exists(Path.Combine(imported, "App.de[1].xliff")));
    }

    [Fact]
    public async Task Add_CultureWithBrackets_ReportsNormallyInsteadOfAMarkupErrorAsync()
    {
        // The culture is a bare CLI argument and reaches the spinner label before anything validates it, so a
        // typo used to surface as "Could not find color or style" instead of a real message.
        var bin = Path.Combine(_directory, "bin");
        Directory.CreateDirectory(bin);
        // Something for the scope to scan — a scope with nothing built in it is a failure of its own now, and
        // this test is about the label, which is rendered before any of that.
        await File.WriteAllBytesAsync(Path.Combine(bin, "NotAnAssembly.dll"), [0x4D, 0x5A, 0x00, 0x00]);

        Assert.Equal(0, await ToolApplication.RunAsync(["add", "[x]", "--input", bin, "--output", _directory]));
    }

    [Fact]
    public async Task Export_CultureWithBrackets_BundlesInsteadOfCrashingAsync()
    {
        var catalogs = Path.Combine(_directory, "catalogs");
        Directory.CreateDirectory(catalogs);
        await WriteCatalogAsync(Path.Combine(catalogs, "LibA.[x].arb"), "[x]", ("save", "Speichern"));

        // Both export shapes render the culture: --lang labels the spinner up front, and the fan-out re-labels it
        // per culture group.
        var kit = Path.Combine(_directory, "kit.zip");
        Assert.Equal(0, await ToolApplication.RunAsync(["export", "--input", catalogs, "--lang", "[x]", "--output", kit]));
        Assert.True(File.Exists(kit));

        var kits = Path.Combine(_directory, "kits");
        Assert.Equal(0, await ToolApplication.RunAsync(["export", "--input", catalogs, "--output", kits, "--source", "en"]));
        Assert.True(File.Exists(Path.Combine(kits, "[x].zip")));
    }

    [Fact]
    public async Task Merge_CultureWithBrackets_BundlesInsteadOfCrashingAsync()
    {
        var catalogs = Path.Combine(_directory, "merge-in");
        Directory.CreateDirectory(catalogs);
        await WriteCatalogAsync(Path.Combine(catalogs, "LibA.[x].arb"), "[x]", ("save", "Speichern"));

        var bundles = Path.Combine(_directory, "bundles");

        Assert.Equal(0, await ToolApplication.RunAsync(["merge", "--input", catalogs, "--output", bundles, "--source", "en", "--format", "arb"]));
        Assert.True(File.Exists(Path.Combine(bundles, "[x].arb")));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static async Task<byte[]> SerializeCatalogAsync(string culture, params (string Key, string Message)[] entries)
    {
        var catalog = new Catalog
        {
            Culture = culture,
            Entries =
            [
                .. entries.Select(e => new CatalogEntry
                {
                    Key = e.Key,
                    Category = "App.Labels",
                    SourceMessage = e.Message,
                    TranslatedMessage = e.Message,
                    SourceFingerprint = "fp",
                    State = TranslationState.Translated
                })
            ]
        };

        using var buffer = new MemoryStream();
        await _arb.WriteAsync(buffer, catalog);
        return buffer.ToArray();
    }

    private static async Task WriteCatalogAsync(string path, string culture, params (string Key, string Message)[] entries) =>
        await File.WriteAllBytesAsync(path, await SerializeCatalogAsync(culture, entries));
}
