using ArchPillar.Extensions.Localization.Tooling.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace ArchPillar.Extensions.Localization.Tooling.Tests;

/// <summary>
/// Issue #100: a call site made through an attributed wrapper declared in another assembly used to be silently
/// dropped when that assembly could not be resolved beside the one being read. The same <c>Library.dll</c>, byte
/// for byte, is read from two directories that differ only by whether the declaring assembly sits beside it —
/// pinning both that the loss is now reported rather than silent, and that pointing the scan at the wrapper
/// (what the build's <c>--reference</c> arguments do) recovers the site outright.
/// </summary>
[Collection(ToolInvocationCollection.Name)]
public sealed class UnresolvableWrapperTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "apl-repro-" + Guid.NewGuid().ToString("N"));

    private const string HelperSource = """
        using ArchPillar.Extensions.Localization;

        namespace Helpers;

        public static class ValidationRules
        {
            public static string WithLocalizedMessage<[TranslationScope] T>(
                [Translatable] string key,
                [TranslationDefault] string message) => key + message;
        }
        """;

    private const string LibrarySource = """
        using ArchPillar.Extensions.Localization;
        using Helpers;

        namespace Demo;

        public sealed class Parameters
        {
            public string Consent =>
                ValidationRules.WithLocalizedMessage<Parameters>("TermsConsent.required", "User must consent to terms");
        }

        public sealed class Labels(ILocalizer<Labels> localizer) : Localized<Labels>(localizer)
        {
            public string Save => Translate("Save");
        }
        """;

    // The scope is declared by a base type in another assembly, so reading the category needs that assembly even
    // though the call itself binds to Localized<T>, which always resolves.
    private const string ScopedBaseSource = """
        using ArchPillar.Extensions.Localization;

        namespace Helpers;

        public abstract class ScopedBase<[TranslationScope] TSelf>(ILocalizer<TSelf> localizer) : Localized<TSelf>(localizer)
            where TSelf : ScopedBase<TSelf>;
        """;

    private const string ScopedLibrarySource = """
        using ArchPillar.Extensions.Localization;
        using Helpers;

        namespace Demo;

        public sealed class ScopedLabels(ILocalizer<ScopedLabels> localizer) : ScopedBase<ScopedLabels>(localizer)
        {
            public string Save => Translate("Save");
        }
        """;

    // Named like the framework on purpose: the extractor treats Microsoft.* as a scope that cannot declare a
    // translation contract, so missing it must not count against the scan.
    private const string FrameworkBaseSource = """
        namespace Microsoft.Fake;

        public class FrameworkBase;
        """;

    private const string FrameworkChildSource = """
        using ArchPillar.Extensions.Localization;

        namespace Demo;

        public sealed class Thing : Microsoft.Fake.FrameworkBase
        {
            public string Title => Label("Title", "A title");

            public string Label([Translatable] string key, [TranslationDefault] string text) => text;
        }
        """;

    [Fact]
    public void Extract_WrapperAssemblyBesideTheTarget_FindsBothCallSites()
    {
        var directory = Stage("with", copyHelpers: true);

        IReadOnlyList<RawCallSite> sites = ExtractFrom(Path.Combine(directory, "Library.dll"));

        Assert.Equal(
            ["Save", "TermsConsent.required"],
            sites.Select(site => site.Key).OrderBy(key => key, StringComparer.Ordinal));
    }

    [Fact]
    public void Extract_WrapperAssemblyMissing_ReportsTheCallSiteItCouldNotClassify()
    {
        var directory = Stage("alone", copyHelpers: false);

        CallSiteScan scan = ScanOf(Path.Combine(directory, "Library.dll"));

        // The site still cannot be read — the attributes that would classify it are in the missing assembly —
        // but the scan now says so instead of passing for a complete one. That is what stops the prune.
        RawCallSite only = Assert.Single(scan.Sites);
        Assert.Equal("Save", only.Key);
        Assert.False(scan.IsComplete);
        Assert.Equal(1, scan.UnclassifiedCallCount);
        Assert.Equal("Helpers", Assert.Single(scan.UnresolvedAssemblies));
    }

    [Fact]
    public void Extract_WrapperSuppliedByReferenceDirectory_FindsBothAndIsComplete()
    {
        var alone = Stage("alone", copyHelpers: false);
        var build = Path.Combine(_root, "build");

        // What the build now passes as --reference: the directories it actually compiled against. The wrapper is
        // resolved from where it really lives, so the result no longer depends on what sits beside Library.dll.
        using var extractor = new AssemblyStringExtractor([build]);
        CallSiteScan scan = extractor.Extract(Path.Combine(alone, "Library.dll"), includeAnnotations: false).CallSites;

        Assert.True(scan.IsComplete);
        Assert.Equal(
            ["Save", "TermsConsent.required"],
            scan.Sites.Select(site => site.Key).OrderBy(key => key, StringComparer.Ordinal));
    }

    [Fact]
    public void Extract_AfterReadingAnAssemblyBesideTheWrapper_StillLeaksResolutionIntoTheUnresolvableOne()
    {
        var with = Stage("with", copyHelpers: true);
        var alone = Stage("alone", copyHelpers: false);

        // One extractor for the batch, exactly as ScopeRunner uses it. Reading the directory that HAS the
        // wrapper registers that directory on the shared resolver, and it is never removed — so the very next
        // read of the identical, wrapper-less assembly now resolves through it.
        using var extractor = new AssemblyStringExtractor();
        IReadOnlyList<RawCallSite> first = extractor.Extract(Path.Combine(with, "Library.dll"), includeAnnotations: false).CallSites.Sites;
        IReadOnlyList<RawCallSite> second = extractor.Extract(Path.Combine(alone, "Library.dll"), includeAnnotations: false).CallSites.Sites;

        Assert.Equal(2, first.Count);

        // Still true, and deliberately pinned: scope decides which directories are on the probe list, so the
        // same assembly can extract differently depending on what was scanned before it. The guards above are
        // what make that harmless — the narrow answer is now reported and never prunes — and --reference is how
        // a caller makes it moot. Remove this assertion only when resolution stops depending on scan order.
        Assert.Equal(2, second.Count);
    }

    [Fact]
    public void Extract_ScopeDeclaredOnAnAvailableBaseType_FilesTheCallUnderThatScope()
    {
        var directory = StageScopedBase("with", copyBase: true);

        CallSiteScan scan = ScanOf(Path.Combine(directory, "ScopedLibrary.dll"));

        Assert.True(scan.IsComplete);
        Assert.Equal("Demo.ScopedLabels", Assert.Single(scan.Sites).Category);
    }

    [Fact]
    public void Extract_ScopeDeclaredOnAMissingBaseType_ReportsTheCallInsteadOfFilingItGlobally()
    {
        var directory = StageScopedBase("alone", copyBase: false);

        CallSiteScan scan = ScanOf(Path.Combine(directory, "ScopedLibrary.dll"));

        // The base that names the category is what is missing. Filing the string under the global category
        // would add a new unit and prune the real one, from a scan that claimed to be complete.
        Assert.Empty(scan.Sites);
        Assert.False(scan.IsComplete);
        Assert.Equal("ScopedHelpers", Assert.Single(scan.UnresolvedAssemblies));
    }

    [Fact]
    public void Extract_BaseTypeInAMissingFrameworkAssembly_StaysComplete()
    {
        var directory = StagePair("alone", copyDependency: false, "Microsoft.Fake", FrameworkBaseSource, "FrameworkChild", FrameworkChildSource);

        CallSiteScan scan = ScanOf(Path.Combine(directory, "FrameworkChild.dll"));

        // The framework cannot declare a scope, so a walk ending there lost nothing. Counting it would switch
        // pruning off for any class library whose base types are not copied beside it.
        Assert.True(scan.IsComplete);
        RawCallSite only = Assert.Single(scan.Sites);
        Assert.Equal("Title", only.Key);
        Assert.Equal(string.Empty, only.Category);
    }

    [Fact]
    public async Task Sync_IncompleteScan_KeepsTheTranslationItCouldNotSeeAsync()
    {
        var with = Stage("with", copyHelpers: true);
        var alone = Stage("alone", copyHelpers: false);
        var catalogs = Path.Combine(_root, "catalogs");
        await ToolApplication.RunAsync(["add", "da", "--assembly", Path.Combine(with, "Library.dll"), "--output", catalogs]);

        var exit = await ToolApplication.RunAsync(["sync", "--assembly", Path.Combine(alone, "Library.dll"), "--output", catalogs]);

        // The data loss of issue #100, end to end: this unit is absent from the scan only because the wrapper
        // could not be read, and deleting it is the one mistake the tool cannot undo.
        Assert.Equal(0, exit);
        Assert.Contains("TermsConsent.required", ReadCatalog(catalogs, "da"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Extract_IncompleteScanWithNoExistingCatalog_WritesNothingAndFailsAsync()
    {
        var alone = Stage("alone", copyHelpers: false);
        var catalogs = Path.Combine(_root, "catalogs");

        var exit = await ToolApplication.RunAsync(["extract", "--assembly", Path.Combine(alone, "Library.dll"), "--output", catalogs]);

        // A template file does not record that its scan was incomplete, so once written it reads as the whole
        // truth: `sync --template` would prune every translation it left out. No file, no such sync.
        Assert.NotEqual(0, exit);
        Assert.False(Directory.Exists(catalogs) && Directory.EnumerateFiles(catalogs).Any());
    }

    [Fact]
    public async Task Extract_IncompleteScanOverAnExistingCatalog_KeepsItsEntriesAsync()
    {
        var with = Stage("with", copyHelpers: true);
        var alone = Stage("alone", copyHelpers: false);
        var catalogs = Path.Combine(_root, "catalogs");
        await ToolApplication.RunAsync(["extract", "--assembly", Path.Combine(with, "Library.dll"), "--output", catalogs]);

        var exit = await ToolApplication.RunAsync(["extract", "--assembly", Path.Combine(alone, "Library.dll"), "--output", catalogs]);

        Assert.Equal(0, exit);
        Assert.Contains("TermsConsent.required", ReadCatalog(catalogs, "en"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Extract_ReferenceListFile_ResolvesTheWrapperFromTheDirectoriesItNamesAsync()
    {
        var alone = Stage("alone", copyHelpers: false);
        var catalogs = Path.Combine(_root, "catalogs");
        var list = Path.Combine(_root, "references.txt");
        await File.WriteAllLinesAsync(list, [Path.Combine(_root, "unrelated"), Path.Combine(_root, "build")]);

        var exit = await ToolApplication.RunAsync(
            ["extract", "--assembly", Path.Combine(alone, "Library.dll"), "--reference", "@" + list, "--output", catalogs]);

        Assert.Equal(0, exit);
        Assert.Contains("TermsConsent.required", ReadCatalog(catalogs, "en"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Extract_ReferenceListFileMissing_FailsRatherThanScanningWithoutItAsync()
    {
        var with = Stage("with", copyHelpers: true);
        var catalogs = Path.Combine(_root, "catalogs");

        var exit = await ToolApplication.RunAsync(
            ["extract", "--assembly", Path.Combine(with, "Library.dll"), "--reference", "@" + Path.Combine(_root, "absent.txt"), "--output", catalogs]);

        Assert.NotEqual(0, exit);
    }

    [Fact]
    public async Task ExtractTarget_MoreReferenceDirectoriesThanACommandLineHolds_StillReachesTheToolAsync()
    {
        var alone = Stage("alone", copyHelpers: false);
        var catalogs = Path.Combine(_root, "catalogs");
        var project = WriteExtractProject(Path.Combine(alone, "Library.dll"), catalogs);

        (var exitCode, var output) = await TestToolchain.RunDotnetAsync(
            _root, "msbuild", project, "-t:ArchPillarLocalizationExtractTemplate", "-nologo", "-nodeReuse:false");

        Assert.True(exitCode == 0, output);
        Assert.Contains("TermsConsent.required", ReadCatalog(catalogs, "en"), StringComparison.Ordinal);
    }

    // Compiles Helpers + Library once, then stages Library.dll into its own directory, with or without the
    // declaring assembly beside it. The Library.dll bytes are identical in both.
    private string Stage(string name, bool copyHelpers) =>
        StagePair(name, copyHelpers, "Helpers", HelperSource, "Library", LibrarySource);

    private string StageScopedBase(string name, bool copyBase) =>
        StagePair(name, copyBase, "ScopedHelpers", ScopedBaseSource, "ScopedLibrary", ScopedLibrarySource);

    private string StagePair(
        string name,
        bool copyDependency,
        string dependency,
        string dependencySource,
        string library,
        string librarySource)
    {
        var build = Path.Combine(_root, "build");
        Directory.CreateDirectory(build);
        var dependencyPath = Path.Combine(build, dependency + ".dll");
        if (!File.Exists(dependencyPath))
        {
            Compile(dependency, dependencySource, dependencyPath, []);
            Compile(library, librarySource, Path.Combine(build, library + ".dll"), [dependencyPath]);
        }

        var staged = Path.Combine(_root, name);
        Directory.CreateDirectory(staged);
        File.Copy(Path.Combine(build, library + ".dll"), Path.Combine(staged, library + ".dll"), overwrite: true);
        if (copyDependency)
        {
            File.Copy(dependencyPath, Path.Combine(staged, dependency + ".dll"), overwrite: true);
        }

        return staged;
    }

    // Runs the real extract target against a built assembly, with far more reference directories than a Windows
    // command line holds: the shape of a project with a few hundred package references. Only Helpers.dll exists,
    // so the catalog gaining the wrapper's key proves the list reached the tool rather than merely not failing.
    private string WriteExtractProject(string assemblyPath, string catalogs)
    {
        var repository = TestToolchain.RepositoryRoot();
        var build = Path.Combine(repository, "src", "Localization.Analyzers", "build", "ArchPillar.Extensions.Localization");

        // The tool's own output, not the copy in this test's bin: that one sits beside net10.0 builds of its
        // dependencies, which a net8.0 tool cannot load. The configuration is whichever this test was built in.
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var tool = Path.Combine(repository, "src", "Localization.Tooling", "bin", configuration, "net8.0", "ArchPillar.Extensions.Localization.Tooling.dll");
        var packages = string.Join(
            "\n",
            Enumerable.Range(0, 300).Select(index =>
                $"""<ReferencePath Include="{_root}/packages/a-package-with-a-realistically-long-identifier-{index}/1.0.0/lib/net10.0/Package.dll" />"""));
        var helpers = Path.Combine(_root, "build", "Helpers.dll");
        var project = Path.Combine(_root, "extract.proj");
        File.WriteAllText(project, $"""
            <Project>
              <Import Project="{build}.props" />
              <PropertyGroup>
                <ArchPillarLocalizationExtractTransitively>true</ArchPillarLocalizationExtractTransitively>
                <ArchPillarLocalizationToolCommand>dotnet &quot;{tool}&quot;</ArchPillarLocalizationToolCommand>
                <ArchPillarLocalizationToolContinueOnError>false</ArchPillarLocalizationToolContinueOnError>
                <ArchPillarLocalizationCatalogPath>{catalogs}</ArchPillarLocalizationCatalogPath>
                <BuildingProject>true</BuildingProject>
                <TargetPath>{assemblyPath}</TargetPath>
                <IntermediateOutputPath>obj/</IntermediateOutputPath>
              </PropertyGroup>
              <ItemGroup>
                <ReferencePath Include="{helpers}" />
                {packages}
              </ItemGroup>
              <Import Project="{build}.targets" />
            </Project>
            """);
        return project;
    }

    private static string ReadCatalog(string directory, string culture) =>
        File.ReadAllText(Assert.Single(Directory.GetFiles(directory, $"Library.{culture}.*")));

    private static IReadOnlyList<RawCallSite> ExtractFrom(string assemblyPath) => ScanOf(assemblyPath).Sites;

    private static CallSiteScan ScanOf(string assemblyPath)
    {
        using var extractor = new AssemblyStringExtractor();
        return extractor.Extract(assemblyPath, includeAnnotations: false).CallSites;
    }

    private static void Compile(string name, string source, string outputPath, string[] extraReferences)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => path.Length > 0)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(ILocalizer).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(TranslatableAttribute).Assembly.Location));
        references.AddRange(extraReferences.Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)));

        var compilation = CSharpCompilation.Create(
            name,
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        EmitResult result = compilation.Emit(outputPath);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup: a still-mapped fixture is harmless residue under the temp directory.
        }
    }
}
