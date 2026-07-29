using ArchPillar.Extensions.Localization.Tooling.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace ArchPillar.Extensions.Localization.Tooling.Tests;

/// <summary>
/// Closes the loop end to end: a translatable call carrying an in-paren comment is compiled to an assembly and
/// the same source is scanned, so the built template must attach the comment to the entry — proving the identity
/// join between the IL-extracted <c>(key, default)</c> and the source-scanned literal.
/// </summary>
public sealed class TemplateBuilderCommentTests : IDisposable
{
    private readonly string _root;

    public TemplateBuilderCommentTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "apltemplatecomments-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Build_CallSiteWithInParenComment_AttachesItToTheEntry()
    {
        var assembly = CompileToDisk("""
            using ArchPillar.Extensions.Localization;

            namespace Demo;

            public sealed class Banner(ILocalizer localizer)
            {
                public string Title => localizer.Translate("home.title", "Home" /* shown in the header */);
            }
            """);
        CommentIndex comments = SourceCommentScanner.Scan(_root);

        using var extractor = new AssemblyStringExtractor();
        Catalog template = Assert.IsType<Catalog>(TemplateBuilder.Build(extractor, assembly, "en", comments));

        CatalogEntry entry = Assert.Single(template.Entries, candidate => candidate.Key == "home.title");
        Assert.Equal("shown in the header", entry.Comment);
    }

    private string CompileToDisk(string source)
    {
        File.WriteAllText(Path.Combine(_root, "Banner.cs"), source);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => path.Length > 0)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(ILocalizer).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(TranslatableAttribute).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "TemplateCommentFixture_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var path = Path.Combine(_root, compilation.AssemblyName + ".dll");
        EmitResult result = compilation.Emit(path);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
