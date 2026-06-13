using System.Collections.Immutable;
using ArchPillar.Extensions.Localization.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ArchPillar.Extensions.Localization.Detection.Tests;

internal static class RoslynTestHost
{
    public static Compilation CreateCompilation(string source) => CreateCompilation([source]);

    public static Compilation CreateCompilation(IReadOnlyList<string> sources)
    {
        SyntaxTree[] trees = [.. sources.Select((source, index) => CSharpSyntaxTree.ParseText(source, path: $"File{index}.cs"))];
        var trustedAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        var references = trustedAssemblies
            .Split(Path.PathSeparator)
            .Where(path => path.Length > 0)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(TranslatableAttribute).Assembly.Location));

        return CSharpCompilation.Create(
            "Tests",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    public static Task<ImmutableArray<Diagnostic>> RunAnalyzerAsync(Compilation compilation) =>
        RunAnalyzerAsync(compilation, new Dictionary<string, string>(StringComparer.Ordinal));

    public static async Task<ImmutableArray<Diagnostic>> RunAnalyzerAsync(
        Compilation compilation,
        IReadOnlyDictionary<string, string> globalOptions)
    {
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new TranslationAnalyzer());
        var options = new AnalyzerOptions(
            ImmutableArray<AdditionalText>.Empty,
            new TestOptionsProvider(globalOptions));
        return await compilation.WithAnalyzers(analyzers, options).GetAnalyzerDiagnosticsAsync(CancellationToken.None);
    }

    private sealed class TestOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly TestOptions _options;

        public TestOptionsProvider(IReadOnlyDictionary<string, string> values)
        {
            _options = new TestOptions(values);
        }

        public override AnalyzerConfigOptions GlobalOptions => _options;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _options;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _options;
    }

    private sealed class TestOptions : AnalyzerConfigOptions
    {
        private readonly IReadOnlyDictionary<string, string> _values;

        public TestOptions(IReadOnlyDictionary<string, string> values)
        {
            _values = values;
        }

        public override bool TryGetValue(string key, out string value)
        {
            if (_values.TryGetValue(key, out var found))
            {
                value = found;
                return true;
            }

            value = null!;
            return false;
        }
    }
}
