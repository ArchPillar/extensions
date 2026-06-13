using System.Text;
using ArchPillar.Extensions.Localization.Formats;
using ArchPillar.Extensions.Localization.Tooling.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace ArchPillar.Extensions.Localization.EndToEnd.Tests;

/// <summary>
/// Shared harness for the end-to-end tests: compile a snippet of "developer code", run the real generator,
/// emit a genuine assembly, and recover the source-language template the way the build does — the tool's IL
/// extraction (Decision D-K). This is the entry point of the pipeline every golden-path test starts from.
/// </summary>
internal static class GeneratorPipeline
{
    // The ARB template the build produces for the developer's code: emit a real assembly and run the tool's
    // IL extractor over it — exactly what `dotnet apl extract` does, not a proxy.
    public static string ExtractTemplateArb(string developerCode)
    {
        var directory = Directory.CreateTempSubdirectory("apl-e2e-").FullName;
        try
        {
            var assembly = EmitAssembly(developerCode, "GoldenPath", directory);
            using var extractor = new AssemblyStringExtractor();
            Catalog template = TemplateBuilder.Build(extractor, assembly, "en")
                ?? new Catalog { Culture = "en", Entries = [] };
            return ToArb(template);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Compiles the developer's code, runs the real generator over it, and emits a genuine assembly whose IL
    /// carries the translatable call sites — the input the scope-aware tool extracts from. Returns the path.
    /// </summary>
    public static string EmitAssembly(string developerCode, string assemblyName, string outputDirectory)
    {
        Compilation compilation = Compile(developerCode, assemblyName);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new Generator.TranslationGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation updated, out _);

        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, assemblyName + ".dll");
        using FileStream stream = File.Create(path);
        EmitResult result = updated.Emit(stream);
        if (!result.Success)
        {
            throw new InvalidOperationException("Emit failed: " + string.Join("; ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        }

        return path;
    }

    private static string ToArb(Catalog catalog)
    {
        using var stream = new MemoryStream();
        new ArbTranslationFormat().WriteAsync(stream, catalog, CancellationToken.None).GetAwaiter().GetResult();
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static Compilation Compile(string source, string assemblyName)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, path: "Program.cs");
        var trustedAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        List<MetadataReference> references =
        [
            .. trustedAssemblies
                .Split(Path.PathSeparator)
                .Where(path => path.Length > 0)
                .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
        ];
        references.Add(MetadataReference.CreateFromFile(typeof(TranslatableAttribute).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(ILocalizer<>).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Localization.IStringLocalizer).Assembly.Location));

        return CSharpCompilation.Create(
            assemblyName,
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
