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
    public async Task RunAsync_NoArguments_PrintsUsageWithoutCrashingAsync()
    {
        var exit = await ToolApplication.RunAsync([]);

        Assert.Equal(2, exit);
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

    [Fact]
    public async Task Sync_TypoedCheckOption_IsRejectedAndDoesNotWriteAsync()
    {
        await WriteTemplateAsync();
        await ToolApplication.RunAsync(["add", "de", "--template", _template, "--output", _directory]);
        var targetPath = Path.Combine(_directory, "de.arb");
        var before = await File.ReadAllBytesAsync(targetPath);

        // "--chek" is a typo for "--check"; silently ignoring it would turn the read-only gate into a write.
        var exit = await ToolApplication.RunAsync(["sync", "--template", _template, "--target", targetPath, "--chek"]);

        Assert.Equal(2, exit);
        Assert.Equal(before, await File.ReadAllBytesAsync(targetPath));
    }

    [Fact]
    public async Task Sync_Check_DriftReturns1_DistinctFromErrorCode2Async()
    {
        await WriteTemplateAsync();
        await ToolApplication.RunAsync(["add", "de", "--template", _template, "--output", _directory]);
        var targetPath = Path.Combine(_directory, "de.arb");

        // Drop an entry so the target diverges from a fresh reconcile (which would re-add it); --check must
        // report this drift as 1, distinct from the error code 2.
        Catalog target = await ReadAsync(targetPath);
        await WriteCatalogRawAsync(targetPath, target with { Entries = [target.Entries[0]] });

        var drift = await ToolApplication.RunAsync(["sync", "--template", _template, "--target", targetPath, "--check"]);
        var error = await ToolApplication.RunAsync(["sync", "--template", _template, "--target", Path.Combine(_directory, "missing.arb"), "--check"]);

        Assert.Equal(1, drift);
        Assert.Equal(2, error);
    }

    [Fact]
    public async Task Add_WithoutLanguage_FailsInsteadOfCreatingJunkFileAsync()
    {
        await WriteTemplateAsync();

        var exit = await ToolApplication.RunAsync(["add", "--template", _template, "--output", _directory]);

        Assert.Equal(2, exit);
        Assert.False(File.Exists(Path.Combine(_directory, "--template.arb")));
    }

    [Fact]
    public async Task Sync_MalformedTarget_ErrorNamesTheFileAsync()
    {
        await WriteTemplateAsync();
        var targetPath = Path.Combine(_directory, "de.arb");
        await File.WriteAllTextAsync(targetPath, "{ not valid json");

        TextWriter error = Console.Error;
        using var captured = new StringWriter();
        Console.SetError(captured);
        try
        {
            var exit = await ToolApplication.RunAsync(["sync", "--template", _template, "--target", targetPath]);
            Assert.Equal(2, exit);
        }
        finally
        {
            Console.SetError(error);
        }

        Assert.Contains("de.arb", captured.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Merge_OutputEqualsInput_IsRefusedAsync()
    {
        var input = Path.Combine(_directory, "input");
        Directory.CreateDirectory(input);
        await WriteCatalogAsync(Path.Combine(input, "LibA.de.arb"), "de", ("save", "Speichern"));

        var exit = await ToolApplication.RunAsync(["merge", "--input", input, "--output", input, "--source", "en"]);

        Assert.Equal(2, exit);
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

    private static async Task WriteCatalogRawAsync(string path, Catalog catalog)
    {
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
