using ArchPillar.Extensions.Localization.Tooling.Internal;

namespace ArchPillar.Extensions.Localization.Tooling.Tests;

/// <summary>
/// What a scope actually resolves to. A project or solution names <em>projects</em>, so it must resolve to those
/// projects' own output assemblies — a <c>bin</c> folder is mostly other people's code, since every NuGet
/// dependency and native interop library is copied there. <c>--input</c> is the opposite: it names a directory of
/// built assemblies, so everything under it is in scope by definition.
/// </summary>
public sealed class ScopeResolverTests : IDisposable
{
    private readonly string _root;

    public ScopeResolverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "aplscope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Resolve_Project_TakesOnlyTheProjectsOwnAssemblyNotItsDependencies()
    {
        var project = MakeProject("App.Web");
        WriteAssembly("App.Web", "bin", "Debug", "net10.0", "App.Web.dll");
        // The kind of thing a real bin folder is full of: a package's assembly, and a native library that is not
        // a managed assembly at all.
        WriteAssembly("App.Web", "bin", "Debug", "net10.0", "Google.Ads.GoogleAds.dll");
        WriteAssembly("App.Web", "bin", "Debug", "net10.0", "runtimes", "win-x64", "native", "e_sqlite3.dll");

        IReadOnlyList<string> resolved = ScopeResolver.Resolve(new ScopeOptions(null, null, project, null, Recurse: false));

        Assert.Equal(["App.Web.dll"], resolved.Select(Path.GetFileName));
    }

    [Fact]
    public void Resolve_Project_CoversEveryConfigurationAndTargetFrameworkOfItsOwnAssembly()
    {
        var project = MakeProject("App.Web");
        WriteAssembly("App.Web", "bin", "Debug", "net8.0", "App.Web.dll");
        WriteAssembly("App.Web", "bin", "Release", "net10.0", "App.Web.dll");

        IReadOnlyList<string> resolved = ScopeResolver.Resolve(new ScopeOptions(null, null, project, null, Recurse: false));

        // Deduplicated by file name — the most recently built wins — so multi-targeting yields one candidate.
        Assert.Single(resolved);
        Assert.Equal("App.Web.dll", Path.GetFileName(resolved[0]));
    }

    [Fact]
    public void Resolve_ProjectWithAnAssemblyNameOverride_ResolvesThatAssembly()
    {
        var project = MakeProject("App.Web", assemblyName: "Acme.Web");
        WriteAssembly("App.Web", "bin", "Debug", "net10.0", "Acme.Web.dll");
        WriteAssembly("App.Web", "bin", "Debug", "net10.0", "Newtonsoft.Json.dll");

        IReadOnlyList<string> resolved = ScopeResolver.Resolve(new ScopeOptions(null, null, project, null, Recurse: false));

        Assert.Equal(["Acme.Web.dll"], resolved.Select(Path.GetFileName));
    }

    [Fact]
    public void Resolve_Solution_TakesEachProjectsOwnAssemblyOnly()
    {
        var web = MakeProject("App.Web");
        var core = MakeProject("App.Core");
        WriteAssembly("App.Web", "bin", "Debug", "net10.0", "App.Web.dll");
        WriteAssembly("App.Web", "bin", "Debug", "net10.0", "Google.Ads.GoogleAds.dll");
        WriteAssembly("App.Core", "bin", "Debug", "net10.0", "App.Core.dll");
        var solution = MakeSolution("App.sln", web, core);

        IReadOnlyList<string> resolved = ScopeResolver.Resolve(new ScopeOptions(null, null, null, solution, Recurse: false));

        Assert.Equal(["App.Core.dll", "App.Web.dll"], resolved.Select(Path.GetFileName));
    }

    [Fact]
    public void Resolve_Input_TakesEveryAssemblyUnderTheDirectory()
    {
        // --input names a directory of built assemblies, so its contents are the scope — this is the low-level
        // form, and narrowing it to "project outputs" would leave no way to scan a publish folder.
        var output = Path.Combine(_root, "publish");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "App.Web.dll"), string.Empty);
        File.WriteAllText(Path.Combine(output, "Google.Ads.GoogleAds.dll"), string.Empty);

        IReadOnlyList<string> resolved = ScopeResolver.Resolve(new ScopeOptions(null, output, null, null, Recurse: false));

        Assert.Equal(["App.Web.dll", "Google.Ads.GoogleAds.dll"], resolved.Select(Path.GetFileName));
    }

    [Fact]
    public void Resolve_ProjectWithNoBuildOutput_ResolvesNothingRatherThanThrowing()
    {
        var project = MakeProject("App.Web");

        Assert.Empty(ScopeResolver.Resolve(new ScopeOptions(null, null, project, null, Recurse: false)));
    }

    [Fact]
    public void Resolve_AssemblyNameSetInDirectoryBuildProps_IsEvaluatedNotGuessed()
    {
        // The project file says nothing about its assembly name — it comes from Directory.Build.props, through a
        // property expression. Reading the XML would guess "Lib" and find nothing; only MSBuild knows it is
        // "Zeta.Lib". This is the case that makes evaluation necessary rather than nice to have.
        var area = Path.Combine(_root, "evaluated");
        Directory.CreateDirectory(area);
        File.WriteAllText(
            Path.Combine(area, "Directory.Build.props"),
            "<Project><PropertyGroup><AssemblyName>Zeta.$(MSBuildProjectName)</AssemblyName></PropertyGroup></Project>");
        var project = MakeBuildableProject(area, "Lib");
        WriteFile(Path.Combine(area, "Lib", "bin", "Debug", "net10.0", "Zeta.Lib.dll"));
        WriteFile(Path.Combine(area, "Lib", "bin", "Debug", "net10.0", "Google.Ads.GoogleAds.dll"));

        IReadOnlyList<string> resolved = ScopeResolver.Resolve(new ScopeOptions(null, null, project, null, Recurse: false));

        Assert.Equal(["Zeta.Lib.dll"], resolved.Select(Path.GetFileName));
    }

    [Fact]
    public void Resolve_ProjectMsBuildCannotEvaluate_FallsBackToTheProjectFileName()
    {
        // No target framework, so the evaluation fails. The scan degrades to the SDK's default naming rather
        // than resolving nothing at all.
        var project = MakeProject("App.Web");
        WriteFile(Path.Combine(_root, "App.Web", "bin", "Debug", "net10.0", "App.Web.dll"));

        IReadOnlyList<string> resolved = ScopeResolver.Resolve(new ScopeOptions(null, null, project, null, Recurse: false));

        Assert.Equal(["App.Web.dll"], resolved.Select(Path.GetFileName));
    }

    [Fact]
    public void Extract_FileThatIsNotAManagedAssembly_IsSkippedInsteadOfFailingTheScan()
    {
        // A native library, or anything else with a .dll name that Cecil cannot open. A scan walks whatever is
        // on disk, so such a file is simply not a candidate — it must never abort the assemblies around it.
        var native = Path.Combine(_root, "e_sqlite3.dll");
        File.WriteAllBytes(native, [0x4D, 0x5A, 0x00, 0x00, 0xFF, 0xFF]);

        using var extractor = new AssemblyStringExtractor();
        (IReadOnlyList<RawCallSite> callSites, IReadOnlyList<RawCallSite> annotations) = extractor.Extract(native, includeAnnotations: true);

        Assert.Empty(callSites);
        Assert.Empty(annotations);
    }

    [Fact]
    public void Build_FileThatIsNotAManagedAssembly_YieldsNoTemplate()
    {
        var native = Path.Combine(_root, "libSkiaSharp.dll");
        File.WriteAllBytes(native, [0x00, 0x01, 0x02, 0x03]);

        using var extractor = new AssemblyStringExtractor();

        Assert.Null(TemplateBuilder.Build(extractor, native, "en"));
    }

    private string MakeProject(string name, string? assemblyName = null)
    {
        var directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name + ".csproj");
        var body = assemblyName is null
            ? "<Project Sdk=\"Microsoft.NET.Sdk\" />"
            : $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><AssemblyName>{assemblyName}</AssemblyName></PropertyGroup></Project>";
        File.WriteAllText(path, body);
        return path;
    }

    private string MakeSolution(string name, params string[] projects)
    {
        var path = Path.Combine(_root, name);
        IEnumerable<string> lines = projects.Select(project =>
            $"Project(\"{{GUID}}\") = \"{Path.GetFileNameWithoutExtension(project)}\", \"{Path.GetRelativePath(_root, project)}\", \"{{GUID2}}\"");
        File.WriteAllLines(path, lines);
        return path;
    }

    // A project MSBuild can actually evaluate: it declares a target framework, so -getProperty resolves.
    private static string MakeBuildableProject(string area, string name)
    {
        var directory = Path.Combine(area, name);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name + ".csproj");
        File.WriteAllText(
            path,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        return path;
    }

    private void WriteAssembly(string project, params string[] segments) =>
        WriteFile(Path.Combine([_root, project, .. segments]));

    private static void WriteFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
