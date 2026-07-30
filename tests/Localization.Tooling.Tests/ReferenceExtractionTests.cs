using System.Text;
using ArchPillar.Extensions.Localization.Tooling.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace ArchPillar.Extensions.Localization.Tooling.Tests;

/// <summary>
/// Drives source-file references against a real, freshly compiled assembly and its portable PDB. An entry records
/// the files its string is used in — project-relative and without line numbers (Decision D-N) — recovered from the
/// sequence point in effect at each call. A call the PDB cannot attribute (no symbols, a hidden point) simply has
/// no reference; an annotation never has one, since a field or an attribute has no debug location at all.
/// </summary>
public sealed class ReferenceExtractionTests : IDisposable
{
    private readonly string _root;

    public ReferenceExtractionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "aplrefs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Extract_CallSite_RecordsTheProjectRelativeFile()
    {
        var assembly = Compile(withPdb: true, ("Ui/Banner.cs", Banner("home.title", "Home")));

        CatalogEntry entry = Single(BuildTemplate(assembly));

        SourceReference reference = Assert.Single(entry.References);
        Assert.Equal("Ui/Banner.cs", reference.FilePath);
        // File-only: no line or column is recorded, so an edit that shifts lines never churns the catalog.
        Assert.Equal(0, reference.Line);
        Assert.Equal(0, reference.Column);
    }

    [Fact]
    public void Extract_SameKeyInTwoFiles_UnionsTheReferencesInOrder()
    {
        var assembly = Compile(
            withPdb: true,
            ("Ui/Second.cs", Banner("home.title", "Home", "Second")),
            ("Ui/First.cs", Banner("home.title", "Home", "First")));

        CatalogEntry entry = Single(BuildTemplate(assembly));

        Assert.Equal(["Ui/First.cs", "Ui/Second.cs"], entry.References.Select(reference => reference.FilePath));
    }

    [Fact]
    public void Extract_SameKeyTwiceInOneFile_RecordsTheFileOnce()
    {
        var assembly = Compile(withPdb: true, ("Ui/Banner.cs", """
            using ArchPillar.Extensions.Localization;

            namespace Demo;

            public sealed class Banner(ILocalizer localizer)
            {
                public string Title => localizer.Translate("home.title", "Home");
                public string Again => localizer.Translate("home.title", "Home");
            }
            """));

        CatalogEntry entry = Single(BuildTemplate(assembly));

        SourceReference reference = Assert.Single(entry.References);
        Assert.Equal("Ui/Banner.cs", reference.FilePath);
    }

    [Fact]
    public void Extract_WithoutSymbols_YieldsNoReferenceAndDoesNotThrow()
    {
        var assembly = Compile(withPdb: false, ("Ui/Banner.cs", Banner("home.title", "Home")));

        CatalogEntry entry = Single(BuildTemplate(assembly));

        Assert.Empty(entry.References);
        Assert.Equal("home.title", entry.Key);
    }

    [Fact]
    public void Extract_HiddenSequencePoint_YieldsNoReference()
    {
        // #line hidden is what a generated view emits around the code between its markup expressions. Reading
        // through it to an earlier point would attribute the call to an unrelated line, so it yields nothing.
        var assembly = Compile(withPdb: true, ("Ui/Banner.cs", """
            using ArchPillar.Extensions.Localization;

            namespace Demo;

            public sealed class Banner(ILocalizer localizer)
            {
            #line hidden
                public string Title => localizer.Translate("home.title", "Home");
            #line default
            }
            """));

        CatalogEntry entry = Single(BuildTemplate(assembly));

        Assert.Empty(entry.References);
    }

    [Fact]
    public void Extract_OutsideTheSourceRoot_DropsTheReference()
    {
        // A deterministic /pathmap build maps documents to paths that are not under the project. An absolute,
        // machine-specific path is never recorded — no reference is the honest answer.
        var assembly = Compile(withPdb: true, ("Ui/Banner.cs", Banner("home.title", "Home")));

        CatalogEntry entry = Single(BuildTemplateWithRoot(assembly, Path.Combine(_root, "elsewhere")));

        Assert.Empty(entry.References);
    }

    [Fact]
    public void Extract_ReferencesNotOptedIn_RecordsNone()
    {
        // References are opt-in (--references / ArchPillarLocalizationExtractReferences): with no root to record
        // them against, none are recorded, even though the PDB could attribute every call.
        var assembly = Compile(withPdb: true, ("Ui/Banner.cs", Banner("home.title", "Home")));

        CatalogEntry entry = Single(BuildTemplateWithRoot(assembly, referenceRoot: null));

        Assert.Empty(entry.References);
    }

    [Fact]
    public void Extract_Annotation_HasNoReference()
    {
        // A property/field carries no debug information — only method bodies do — so an annotation can never be
        // located from the PDB.
        var assembly = Compile(withPdb: true, ("Models/RegisterModel.cs", """
            using System.ComponentModel;

            namespace Demo;

            public sealed class RegisterModel
            {
                [DisplayName("Email address")]
                public string Email { get; set; } = "";
            }
            """));

        CatalogEntry entry = Single(BuildTemplate(assembly));

        Assert.Equal("Email address", entry.Key);
        Assert.Empty(entry.References);
    }

    private static string Banner(string key, string message, string type = "Banner") => $$"""
        using ArchPillar.Extensions.Localization;

        namespace Demo;

        public sealed class {{type}}(ILocalizer localizer)
        {
            public string Title => localizer.Translate("{{key}}", "{{message}}");
        }
        """;

    private Catalog BuildTemplate(string assemblyPath) => BuildTemplateWithRoot(assemblyPath, _root);

    private static Catalog BuildTemplateWithRoot(string assemblyPath, string? referenceRoot)
    {
        using var extractor = new AssemblyStringExtractor();
        return Assert.IsType<Catalog>(
            TemplateBuilder.Build(extractor, assemblyPath, "en", comments: null, referenceRoot: referenceRoot));
    }

    private static CatalogEntry Single(Catalog catalog) => Assert.Single(catalog.Entries);

    // Compiles the given sources — written to real files under the root, so the PDB's documents are the paths
    // references are resolved against — into an assembly, with or without a portable PDB beside it.
    private string Compile(bool withPdb, params (string FileName, string Source)[] files)
    {
        var trees = new List<SyntaxTree>();
        foreach ((var fileName, var source) in files)
        {
            var filePath = Path.Combine(_root, fileName.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, source);
            // The text must carry an encoding or the compiler refuses to emit debug information (CS8055).
            trees.Add(CSharpSyntaxTree.ParseText(SourceText.From(source, Encoding.UTF8), path: filePath));
        }

        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => path.Length > 0)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(ILocalizer).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(TranslatableAttribute).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "ReferenceFixture_" + Guid.NewGuid().ToString("N"),
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var assemblyPath = Path.Combine(_root, compilation.AssemblyName + ".dll");
        EmitResult result;
        using (FileStream peStream = File.Create(assemblyPath))
        {
            if (withPdb)
            {
                using FileStream pdbStream = File.Create(Path.ChangeExtension(assemblyPath, ".pdb"));
                result = compilation.Emit(peStream, pdbStream, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
            }
            else
            {
                result = compilation.Emit(peStream);
            }
        }

        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return assemblyPath;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
