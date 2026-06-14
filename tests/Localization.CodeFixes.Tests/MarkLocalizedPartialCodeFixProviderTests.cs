using System.Collections.Immutable;
using ArchPillar.Extensions.Localization.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace ArchPillar.Extensions.Localization.CodeFixes.Tests;

public sealed class MarkLocalizedPartialCodeFixProviderTests
{
    [Fact]
    public async Task Fix_NonPartialBundle_AddsPartialModifierAsync()
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

        var fixedSource = await ApplyFixAsync(Source);

        Assert.Contains("public sealed partial class TodoStrings", fixedSource, StringComparison.Ordinal);
    }

    private static async Task<string> ApplyFixAsync(string source)
    {
        using var workspace = new AdhocWorkspace();
        Project project = workspace
            .AddProject("Test", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(References());
        Document document = project.AddDocument("Test.cs", source);

        Diagnostic diagnostic = await SingleDiagnosticAsync(document);

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None);
        await new MarkLocalizedPartialCodeFixProvider().RegisterCodeFixesAsync(context);

        CodeAction fix = Assert.Single(actions);
        ImmutableArray<CodeActionOperation> operations = await fix.GetOperationsAsync(CancellationToken.None);
        Solution changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        SourceText text = await changed.GetDocument(document.Id)!.GetTextAsync();
        return text.ToString();
    }

    private static async Task<Diagnostic> SingleDiagnosticAsync(Document document)
    {
        Compilation compilation = (await document.Project.GetCompilationAsync())!;
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new TranslationAnalyzer());
        ImmutableArray<Diagnostic> diagnostics =
            await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync(CancellationToken.None);
        return diagnostics.Single(diagnostic => diagnostic.Id == "APL0010");
    }

    private static IEnumerable<MetadataReference> References()
    {
        var trusted = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        foreach (var path in trusted.Split(Path.PathSeparator))
        {
            if (path.Length > 0)
            {
                yield return MetadataReference.CreateFromFile(path);
            }
        }

        yield return MetadataReference.CreateFromFile(typeof(TranslatableAttribute).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(Localized<>).Assembly.Location);
        yield return MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location);
    }
}
