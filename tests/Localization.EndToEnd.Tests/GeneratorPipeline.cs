using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ArchPillar.Extensions.Localization.EndToEnd.Tests;

/// <summary>
/// Shared harness for the end-to-end tests: compile a snippet of "developer code" and run the real source
/// generator over it, returning the ARB template the build would bake. This is the entry point of the
/// pipeline every golden-path test starts from.
/// </summary>
internal static class GeneratorPipeline
{
    // Runs the real source generator over the developer's code and returns the ARB the build extracts. The
    // generator bakes it as base64 into a [GeneratedLocalizationTemplate] assembly attribute; decoding it here
    // is the faithful equivalent of what the dotnet tool reads from the compiled assembly at sync time.
    public static string ExtractTemplateArb(string developerCode)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new Generator.TranslationGenerator());
        GeneratorDriverRunResult result = driver.RunGenerators(Compile(developerCode)).GetRunResult();
        var template = result.Results
            .SelectMany(r => r.GeneratedSources)
            .Single(s => s.HintName == "LocalizationTemplate.g.cs")
            .SourceText
            .ToString();

        const string Marker = "GeneratedLocalizationTemplate(";
        var open = template.IndexOf(Marker, StringComparison.Ordinal) + Marker.Length;
        var close = template.IndexOf(")]", open, StringComparison.Ordinal);
        var parts = template[open..close].Split([", "], StringSplitOptions.None);
        var base64 = parts[2].Trim().Trim('"');
        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }

    private static Compilation Compile(string source)
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

        return CSharpCompilation.Create(
            "GoldenPath",
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
