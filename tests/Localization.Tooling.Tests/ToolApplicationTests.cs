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

    public void Dispose() => Directory.Delete(_directory, recursive: true);

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
