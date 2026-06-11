using ArchPillar.Extensions.Localization.Formats;
using ArchPillar.Extensions.Localization.Tooling;

namespace ArchPillar.Extensions.Localization.EndToEnd.Tests;

/// <summary>
/// The app-scale authoring flow against real assemblies: two libraries each bake a template, and the tool
/// discovers, extracts, adds a language, and checks every assembly in one scoped invocation — never one
/// assembly at a time. Catalogs are named <c>{AssemblyName}.{culture}.arb</c> so the two libraries never
/// collide.
/// </summary>
public sealed class ScopeToolingTests : IDisposable
{
    private const string LibAStrings = """
        using ArchPillar.Extensions.Localization;
        public static class T
        {
            public static string Translate([Translatable] string key, [TranslationDefault] string message) => message;
        }
        public sealed class Save { public string Label() => T.Translate("save", "Save"); }
        """;

    private const string LibBStrings = """
        using ArchPillar.Extensions.Localization;
        public static class T
        {
            public static string Translate([Translatable] string key, [TranslationDefault] string message) => message;
        }
        public sealed class Cancel { public string Label() => T.Translate("cancel", "Cancel"); }
        """;

    private readonly string _root;
    private readonly string _binDirectory;
    private readonly string _catalogs;

    public ScopeToolingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "apl-scope-" + Guid.NewGuid().ToString("N"));
        _binDirectory = Path.Combine(_root, "bin");
        _catalogs = Path.Combine(_root, "Translations");
        GeneratorPipeline.EmitAssemblyWithTemplate(LibAStrings, "LibA", _binDirectory);
        GeneratorPipeline.EmitAssemblyWithTemplate(LibBStrings, "LibB", _binDirectory);
    }

    [Fact]
    public async Task ScopedExtractAddSync_HandlesEveryAssemblyInOneInvocationAsync()
    {
        // One extract over the whole output tree writes a per-assembly template for each library.
        Assert.Equal(0, await ToolApplication.RunAsync(["extract", "--input", _binDirectory, "--output", _catalogs]));
        Assert.True(File.Exists(Path.Combine(_catalogs, "LibA.en.arb")));
        Assert.True(File.Exists(Path.Combine(_catalogs, "LibB.en.arb")));

        // One add creates the German file for every assembly that has strings.
        Assert.Equal(0, await ToolApplication.RunAsync(["add", "de", "--input", _binDirectory, "--output", _catalogs]));
        Catalog libADe = await ReadAsync(Path.Combine(_catalogs, "LibA.de.arb"));
        Assert.Equal("de", libADe.Culture);
        Assert.Equal("save", Assert.Single(libADe.Entries).Key);
        Assert.True(File.Exists(Path.Combine(_catalogs, "LibB.de.arb")));

        // Just-added catalogs are in sync, so the scoped gate is green across all of them.
        Assert.Equal(0, await ToolApplication.RunAsync(["sync", "--input", _binDirectory, "--output", _catalogs, "--check"]));
    }

    [Fact]
    public async Task ScopedSyncCheck_FlagsDriftWhenALibrarysCatalogIsStaleAsync()
    {
        await ToolApplication.RunAsync(["extract", "--input", _binDirectory, "--output", _catalogs]);
        await ToolApplication.RunAsync(["add", "de", "--input", _binDirectory, "--output", _catalogs]);

        // A translator (or merge) drops an entry from one library's catalog; the scoped check must catch it.
        var libBDe = Path.Combine(_catalogs, "LibB.de.arb");
        Catalog stale = await ReadAsync(libBDe);
        await WriteAsync(libBDe, stale with { Entries = [] });

        Assert.Equal(1, await ToolApplication.RunAsync(["sync", "--input", _binDirectory, "--output", _catalogs, "--check"]));
    }

    [Fact]
    public async Task ScopedAdd_SkipsAssembliesThatAlreadyHaveTheLanguageAsync()
    {
        await ToolApplication.RunAsync(["extract", "--input", _binDirectory, "--output", _catalogs]);
        await ToolApplication.RunAsync(["add", "de", "--input", _binDirectory, "--output", _catalogs]);

        // Translate LibA, then add de again: the existing file must be left untouched (not reset to untranslated).
        var libADe = Path.Combine(_catalogs, "LibA.de.arb");
        Catalog catalog = await ReadAsync(libADe);
        await WriteAsync(libADe, catalog with { Entries = [.. catalog.Entries.Select(e => e with { TranslatedMessage = "Speichern", State = TranslationState.Translated })] });

        Assert.Equal(0, await ToolApplication.RunAsync(["add", "de", "--input", _binDirectory, "--output", _catalogs]));

        Catalog after = await ReadAsync(libADe);
        Assert.Equal("Speichern", Assert.Single(after.Entries).TranslatedMessage);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static async Task<Catalog> ReadAsync(string path)
    {
        var format = new ArbTranslationFormat();
        using var stream = new MemoryStream(File.ReadAllBytes(path));
        return await format.ReadAsync(stream, CancellationToken.None);
    }

    private static async Task WriteAsync(string path, Catalog catalog)
    {
        var format = new ArbTranslationFormat();
        using FileStream stream = File.Create(path);
        await format.WriteAsync(stream, catalog, CancellationToken.None);
    }
}
