using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ArchPillar.Extensions.Localization.Detection.Tests;

public sealed class LocalizedBundlePartialAnalyzerTests
{
    [Fact]
    public async Task NonPartialConstructorlessBundle_WithDependencyInjection_ReportsApl0010Async()
    {
        const string Source = """
            using ArchPillar.Extensions.Localization;

            namespace Acme
            {
                public sealed class TodoStrings : Localized<TodoStrings>
                {
                    public string Title => Translate("My Tasks");
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source, withDependencyInjection: true);

        Diagnostic diagnostic = Assert.Single(diagnostics, candidate => candidate.Id == "APL0010");
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Fact]
    public async Task PartialBundle_WithDependencyInjection_DoesNotReportAsync()
    {
        const string Source = """
            using ArchPillar.Extensions.Localization;

            namespace Acme
            {
                public sealed partial class TodoStrings : Localized<TodoStrings>
                {
                    public string Title => Translate("My Tasks");
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source, withDependencyInjection: true);

        Assert.DoesNotContain(diagnostics, candidate => candidate.Id == "APL0010");
    }

    [Fact]
    public async Task BundleWithInjectableConstructor_WithDependencyInjection_DoesNotReportAsync()
    {
        const string Source = """
            using ArchPillar.Extensions.Localization;

            namespace Acme
            {
                public sealed class TodoStrings(ILocalizer<TodoStrings> localizer) : Localized<TodoStrings>(localizer)
                {
                    public string Title => Translate("My Tasks");
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source, withDependencyInjection: true);

        Assert.DoesNotContain(diagnostics, candidate => candidate.Id == "APL0010");
    }

    [Fact]
    public async Task NonPartialConstructorlessBundle_WithoutDependencyInjection_DoesNotReportAsync()
    {
        const string Source = """
            using ArchPillar.Extensions.Localization;

            namespace Acme
            {
                public sealed class TodoStrings : Localized<TodoStrings>
                {
                    public string Title => Translate("My Tasks");
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source, withDependencyInjection: false);

        Assert.DoesNotContain(diagnostics, candidate => candidate.Id == "APL0010");
    }

    private static Task<ImmutableArray<Diagnostic>> RunAsync(string source, bool withDependencyInjection)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source);
        var trustedAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        IEnumerable<string> paths = trustedAssemblies.Split(Path.PathSeparator).Where(path => path.Length > 0);

        // This test project references the DI abstractions, so IServiceCollection rides in through the trusted
        // platform assemblies. Drop them to model a consumer that does not use DI at all.
        if (!withDependencyInjection)
        {
            paths = paths.Where(path => !path.Contains("Microsoft.Extensions.DependencyInjection", StringComparison.Ordinal));
        }

        List<MetadataReference> references =
        [
            .. paths.Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)),
            MetadataReference.CreateFromFile(typeof(TranslatableAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Localized<>).Assembly.Location)
        ];

        var compilation = CSharpCompilation.Create(
            "AnalyzerProbe",
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return RoslynTestHost.RunAnalyzerAsync(compilation);
    }
}
