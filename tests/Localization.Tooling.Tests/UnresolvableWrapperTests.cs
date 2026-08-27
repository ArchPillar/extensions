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

    // Compiles Helpers + Library once, then stages Library.dll into its own directory, with or without the
    // declaring assembly beside it. The Library.dll bytes are identical in both.
    private string Stage(string name, bool copyHelpers)
    {
        var build = Path.Combine(_root, "build");
        Directory.CreateDirectory(build);
        var helpers = Path.Combine(build, "Helpers.dll");
        if (!File.Exists(helpers))
        {
            Compile("Helpers", HelperSource, helpers, []);
            Compile("Library", LibrarySource, Path.Combine(build, "Library.dll"), [helpers]);
        }

        var staged = Path.Combine(_root, name);
        Directory.CreateDirectory(staged);
        File.Copy(Path.Combine(build, "Library.dll"), Path.Combine(staged, "Library.dll"), overwrite: true);
        if (copyHelpers)
        {
            File.Copy(helpers, Path.Combine(staged, "Helpers.dll"), overwrite: true);
        }

        return staged;
    }

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
