using ArchPillar.Extensions.Localization.Formats;

namespace ArchPillar.Extensions.Localization.Tooling.Tests;

public sealed class ToolApplicationTests : IDisposable
{
    private static readonly ArbTranslationFormat _arb = new();

    private readonly string _directory;
    private readonly string _template;

    public ToolApplicationTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "apltool-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _template = Path.Combine(_directory, "en.arb");
    }

    [Fact]
    public async Task Add_CreatesTargetCatalogWithUntranslatedEntriesAsync()
    {
        await WriteTemplateAsync();

        var exit = await ToolApplication.RunAsync(["add", "de", "--template", _template, "--output", _directory]);

        Assert.Equal(0, exit);
        var targetPath = Path.Combine(_directory, "de.arb");
        Assert.True(File.Exists(targetPath));

        Catalog target = await ReadAsync(targetPath);
        Assert.Equal("de", target.Culture);
        Assert.All(target.Entries, entry => Assert.Equal(TranslationState.NeedsTranslation, entry.State));
    }

    [Fact]
    public async Task Sync_Check_PassesWhenTargetIsUpToDateAsync()
    {
        await WriteTemplateAsync();
        await ToolApplication.RunAsync(["add", "de", "--template", _template, "--output", _directory]);
        var targetPath = Path.Combine(_directory, "de.arb");

        var exit = await ToolApplication.RunAsync(["sync", "--template", _template, "--target", targetPath, "--check"]);

        Assert.Equal(0, exit);
    }

    [Fact]
    public async Task Convert_RewritesCatalogInAnotherFormatAsync()
    {
        await WriteTemplateAsync();
        var xliffPath = Path.Combine(_directory, "en.xliff");

        var exit = await ToolApplication.RunAsync(["convert", "--from", _template, "--to", "xliff", "--output", xliffPath]);

        Assert.Equal(0, exit);
        Assert.True(File.Exists(xliffPath));
    }

    [Fact]
    public async Task Merge_FlattensToOneBundlePerCulture_SkipsSourceAsync()
    {
        var input = Path.Combine(_directory, "input");
        var output = Path.Combine(_directory, "out");
        Directory.CreateDirectory(input);
        await WriteCatalogAsync(Path.Combine(input, "LibA.de.arb"), "de", ("save", "Speichern"));
        await WriteCatalogAsync(Path.Combine(input, "LibB.de.arb"), "de", ("cancel", "Abbrechen"));
        await WriteCatalogAsync(Path.Combine(input, "LibA.fr.arb"), "fr", ("save", "Sauvegarder"));
        await WriteCatalogAsync(Path.Combine(input, "LibA.en.arb"), "en", ("save", "Save"));

        var exit = await ToolApplication.RunAsync(["merge", "--input", input, "--output", output, "--source", "en"]);

        Assert.Equal(0, exit);
        Assert.True(File.Exists(Path.Combine(output, "de.arb")));
        Assert.True(File.Exists(Path.Combine(output, "fr.arb")));
        Assert.False(File.Exists(Path.Combine(output, "en.arb"))); // source culture skipped

        Catalog de = await ReadAsync(Path.Combine(output, "de.arb"));
        Assert.Equal(2, de.Entries.Count); // both libraries' de entries merged into one bundle
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private async Task WriteCatalogAsync(string path, string culture, params (string Key, string Message)[] entries)
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

        using FileStream stream = File.Create(path);
        await _arb.WriteAsync(stream, catalog, CancellationToken.None);
    }

    private async Task WriteTemplateAsync()
    {
        var catalog = new Catalog
        {
            Culture = "en",
            Entries =
            [
                new CatalogEntry { Key = "home", SourceMessage = "Home", SourceFingerprint = "fp1" },
                new CatalogEntry { Key = "about", SourceMessage = "About", SourceFingerprint = "fp2" }
            ]
        };

        using FileStream stream = File.Create(_template);
        await _arb.WriteAsync(stream, catalog, CancellationToken.None);
    }

    private static async Task<Catalog> ReadAsync(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return await _arb.ReadAsync(stream, CancellationToken.None);
    }
}
