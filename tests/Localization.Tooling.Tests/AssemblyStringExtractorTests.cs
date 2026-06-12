using ArchPillar.Extensions.Localization.Tooling.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace ArchPillar.Extensions.Localization.Tooling.Tests;

/// <summary>
/// Drives the IL extractor against a real, freshly compiled assembly so the category each call site is
/// attributed to is verified against compiled metadata — the path the source-level detector cannot exercise.
/// The category must match what the Roslyn detector and the runtime derive: the type argument bound to a
/// <c>[TranslationScope]</c> parameter, found on the receiver itself (<c>ILocalizer&lt;T&gt;</c>) or any base
/// type (<c>Localized&lt;T&gt;</c>), with the framework <c>IStringLocalizer&lt;T&gt;</c> falling back to its
/// own type argument.
/// </summary>
public sealed class AssemblyStringExtractorTests : IDisposable
{
    private readonly List<string> _emitted = [];

    [Fact]
    public void Extract_LocalizedBase_TakesCategoryFromTheDerivingType()
    {
        IReadOnlyList<RawCallSite> sites = Extract("""
            using ArchPillar.Extensions.Localization;

            namespace Demo;

            public sealed class Labels(ILocalizer<Labels> localizer) : Localized<Labels>(localizer)
            {
                public string Save => Translate("Save");
            }
            """);

        RawCallSite site = Assert.Single(sites);
        Assert.Equal("Save", site.Key);
        Assert.Equal("Demo.Labels", site.Category);
    }

    [Fact]
    public void Extract_GenericLocalizerReceiver_TakesCategoryFromTheTypeArgument()
    {
        IReadOnlyList<RawCallSite> sites = Extract("""
            using ArchPillar.Extensions.Localization;

            namespace Demo;

            public sealed class Checkout(ILocalizer<Checkout> localizer)
            {
                public string Pay => localizer.Translate("pay", "Pay now");
            }
            """);

        RawCallSite site = Assert.Single(sites);
        Assert.Equal("pay", site.Key);
        Assert.Equal("Demo.Checkout", site.Category);
    }

    [Fact]
    public void Extract_NonGenericReceiver_IsTheGlobalCategory()
    {
        IReadOnlyList<RawCallSite> sites = Extract("""
            using ArchPillar.Extensions.Localization;

            namespace Demo;

            public sealed class Banner(ILocalizer localizer)
            {
                public string Title => localizer.Translate("title", "Home");
            }
            """);

        RawCallSite site = Assert.Single(sites);
        Assert.Equal("title", site.Key);
        Assert.Equal(string.Empty, site.Category);
    }

    [Fact]
    public void Extract_StringLocalizerIndexer_FallsBackToTheTypeArgument()
    {
        IReadOnlyList<RawCallSite> sites = Extract("""
            using Microsoft.Extensions.Localization;

            namespace Demo;

            public sealed class Legacy(IStringLocalizer<Legacy> localizer)
            {
                public string Welcome => localizer["Welcome"];
            }
            """);

        RawCallSite site = Assert.Single(sites);
        Assert.Equal("Welcome", site.Key);
        Assert.Equal("Demo.Legacy", site.Category);
    }

    [Fact]
    public void Build_AssemblyWithNoTranslatableCalls_ProducesNoTemplate()
    {
        // A project that references the localizer but has no translatable call site yields no template, so the
        // build writes no empty {Assembly}.{source}.arb — an empty translation file is pure noise.
        var path = Compile("""
            using ArchPillar.Extensions.Localization;

            namespace Demo;

            public sealed class Service(ILocalizer localizer)
            {
                public ILocalizer Localizer => localizer; // referenced, but never used to translate
            }
            """);

        using var extractor = new AssemblyStringExtractor();
        Assert.Null(TemplateBuilder.Build(extractor, path, "en"));
    }

    private IReadOnlyList<RawCallSite> Extract(string source)
    {
        var path = Compile(source);
        using var extractor = new AssemblyStringExtractor();
        return extractor.Extract(path);
    }

    // Compiles the fixture beside the ArchPillar reference assemblies (the test output directory), so the
    // extractor's resolver can load the base types it reads the [TranslationScope] attribute from.
    private string Compile(string source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => path.Length > 0)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(ILocalizer).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(TranslatableAttribute).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Localization.IStringLocalizer).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "Fixture_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var path = Path.Combine(AppContext.BaseDirectory, compilation.AssemblyName + ".dll");
        EmitResult result = compilation.Emit(path);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        _emitted.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var path in _emitted)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Best-effort cleanup: a still-mapped fixture is harmless test residue under the output directory.
            }
        }
    }
}
