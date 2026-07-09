using System.IO.Compression;
using ArchPillar.Extensions.Localization.Formats;
using ArchPillar.Extensions.Localization.Tooling;

namespace ArchPillar.Extensions.Localization.EndToEnd.Tests;

/// <summary>
/// The app-scale authoring flow against real assemblies: two libraries each bake a template, and the tool
/// discovers, extracts, adds a language, and checks every assembly in one scoped invocation — never one
/// assembly at a time. Catalogs are named <c>{AssemblyName}.{culture}.xliff</c> so the two libraries never
/// collide.
/// </summary>
public sealed class ScopeToolingTests : IDisposable
{
    private const string LibAStrings = """
        using ArchPillar.Extensions.Localization;
        public sealed class Save;
        public sealed class Consumer { public string Label(ILocalizer<Save> loc) => loc.Translate("save", "Save"); }
        """;

    private const string LibBStrings = """
        using ArchPillar.Extensions.Localization;
        public sealed class Cancel;
        public sealed class Consumer { public string Label(ILocalizer<Cancel> loc) => loc.Translate("cancel", "Cancel"); }
        """;

    private readonly string _root;
    private readonly string _binDirectory;
    private readonly string _catalogs;

    public ScopeToolingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "apl-scope-" + Guid.NewGuid().ToString("N"));
        _binDirectory = Path.Combine(_root, "bin");
        _catalogs = Path.Combine(_root, "Translations");
        GeneratorPipeline.EmitAssembly(LibAStrings, "LibA", _binDirectory);
        GeneratorPipeline.EmitAssembly(LibBStrings, "LibB", _binDirectory);
    }

    [Fact]
    public async Task ScopedExtractAddSync_HandlesEveryAssemblyInOneInvocationAsync()
    {
        // One extract over the whole output tree writes a per-assembly template for each library.
        Assert.Equal(0, await ToolApplication.RunAsync(["extract", "--input", _binDirectory, "--output", _catalogs]));
        Assert.True(File.Exists(Path.Combine(_catalogs, "LibA.en.xliff")));
        Assert.True(File.Exists(Path.Combine(_catalogs, "LibB.en.xliff")));

        // One add creates the German file for every assembly that has strings.
        Assert.Equal(0, await ToolApplication.RunAsync(["add", "de", "--input", _binDirectory, "--output", _catalogs]));
        Catalog libADe = Read(Path.Combine(_catalogs, "LibA.de.xliff"));
        Assert.Equal("de", libADe.Culture);
        Assert.Equal("save", Assert.Single(libADe.Entries).Key);
        Assert.True(File.Exists(Path.Combine(_catalogs, "LibB.de.xliff")));

        // Just-added catalogs are in sync, so the scoped gate is green across all of them.
        Assert.Equal(0, await ToolApplication.RunAsync(["sync", "--input", _binDirectory, "--output", _catalogs, "--check"]));
    }

    [Fact]
    public async Task ScopedSyncCheck_FlagsDriftWhenALibrarysCatalogIsStaleAsync()
    {
        await ToolApplication.RunAsync(["extract", "--input", _binDirectory, "--output", _catalogs]);
        await ToolApplication.RunAsync(["add", "de", "--input", _binDirectory, "--output", _catalogs]);

        // A translator (or merge) drops an entry from one library's catalog; the scoped check must catch it.
        var libBDe = Path.Combine(_catalogs, "LibB.de.xliff");
        Catalog stale = Read(libBDe);
        await WriteAsync(libBDe, stale with { Entries = [] });

        Assert.Equal(1, await ToolApplication.RunAsync(["sync", "--input", _binDirectory, "--output", _catalogs, "--check"]));
    }

    [Fact]
    public async Task ScopedAdd_SkipsAssembliesThatAlreadyHaveTheLanguageAsync()
    {
        await ToolApplication.RunAsync(["extract", "--input", _binDirectory, "--output", _catalogs]);
        await ToolApplication.RunAsync(["add", "de", "--input", _binDirectory, "--output", _catalogs]);

        // Translate LibA, then add de again: the existing file must be left untouched (not reset to untranslated).
        var libADe = Path.Combine(_catalogs, "LibA.de.xliff");
        Catalog catalog = Read(libADe);
        await WriteAsync(libADe, catalog with { Entries = [.. catalog.Entries.Select(e => e with { TranslatedMessage = "Speichern", State = TranslationState.Translated })] });

        Assert.Equal(0, await ToolApplication.RunAsync(["add", "de", "--input", _binDirectory, "--output", _catalogs]));

        Catalog after = Read(libADe);
        Assert.Equal("Speichern", Assert.Single(after.Entries).TranslatedMessage);
    }

    [Fact]
    public async Task ProjectScope_DiscoversTheSingleProjectFileInADirectoryAsync()
    {
        File.WriteAllText(Path.Combine(_root, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        // Passing the directory (not the .csproj) finds the single project and scans its bin — like running
        // `dotnet build` in a folder.
        Assert.Equal(0, await ToolApplication.RunAsync(["extract", "--project", _root, "--output", _catalogs]));
        Assert.True(File.Exists(Path.Combine(_catalogs, "LibA.en.xliff")));
        Assert.True(File.Exists(Path.Combine(_catalogs, "LibB.en.xliff")));
    }

    [Fact]
    public async Task ProjectScope_AmbiguousDirectory_IsRejectedRatherThanGuessedAsync()
    {
        File.WriteAllText(Path.Combine(_root, "One.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(Path.Combine(_root, "Two.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        Assert.Equal(2, await ToolApplication.RunAsync(["extract", "--project", _root, "--output", _catalogs]));
    }

    [Fact]
    public async Task SolutionScope_Import_DistributesEachCatalogToItsOwnProjectAsync()
    {
        // Two projects, each in its own subdirectory, referenced by a solution — the layout the authoring commands
        // write per project. A translator's kit carries one catalog per assembly, flat and project-named.
        var libADirectory = Path.Combine(_root, "LibA");
        var libBDirectory = Path.Combine(_root, "LibB");
        Directory.CreateDirectory(libADirectory);
        Directory.CreateDirectory(libBDirectory);
        File.WriteAllText(Path.Combine(libADirectory, "LibA.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(Path.Combine(libBDirectory, "LibB.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var solution = Path.Combine(_root, "App.slnx");
        File.WriteAllText(solution, "<Solution><Project Path=\"LibA/LibA.csproj\" /><Project Path=\"LibB/LibB.csproj\" /></Solution>");

        var kit = Path.Combine(_root, "kit.zip");
        using (ZipArchive zip = ZipFile.Open(kit, ZipArchiveMode.Create))
        {
            await AddCatalogEntryAsync(zip, "LibA.de.xliff");
            await AddCatalogEntryAsync(zip, "LibB.de.xliff");
        }

        // Import with no --output: each returned catalog must land in its own project's Translations folder.
        Assert.Equal(0, await ToolApplication.RunAsync(["import", "--input", kit, "--solution", solution]));

        Assert.True(File.Exists(Path.Combine(libADirectory, "Translations", "LibA.de.xliff")));
        Assert.True(File.Exists(Path.Combine(libBDirectory, "Translations", "LibB.de.xliff")));
    }

    private static async Task AddCatalogEntryAsync(ZipArchive zip, string entryName)
    {
        var catalog = new Catalog
        {
            Culture = "de",
            Entries = [new CatalogEntry { Key = "home", SourceMessage = "Home", TranslatedMessage = "Startseite", SourceFingerprint = "fp", State = TranslationState.Translated }]
        };
        ZipArchiveEntry entry = zip.CreateEntry(entryName);
        using Stream stream = entry.Open();
        await new XliffTranslationFormat().WriteAsync(stream, catalog);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static Catalog Read(string path)
    {
        var format = new XliffTranslationFormat();
        using var stream = new MemoryStream(File.ReadAllBytes(path));
        return format.Read(stream);
    }

    private static async Task WriteAsync(string path, Catalog catalog)
    {
        var format = new XliffTranslationFormat();
        using FileStream stream = File.Create(path);
        await format.WriteAsync(stream, catalog);
    }
}
