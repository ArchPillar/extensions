using System.Collections.Concurrent;
using System.Collections.Immutable;
using ArchPillar.Extensions.Localization.Detection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ArchPillar.Extensions.Localization.Analyzers;

/// <summary>
/// Surfaces, in the editor, the conditions that would otherwise become a silent extraction or runtime
/// bug at a translatable call site. It shares the detection core with the extractor, so the analyzer and
/// the build agree exactly. It is a no-op on projects that do not reference the localization attributes.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TranslationAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Diagnostics.NonConstant, Diagnostics.InvalidMessage, Diagnostics.DuplicateKey);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        if (context.Compilation.GetTypeByMetadataName("ArchPillar.Extensions.Localization.TranslatableAttribute") is null)
        {
            return;
        }

        var defaultsByKey = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        context.RegisterSyntaxNodeAction(
            nodeContext => Analyze(nodeContext, defaultsByKey),
            SyntaxKind.InvocationExpression,
            SyntaxKind.ObjectCreationExpression,
            SyntaxKind.ImplicitObjectCreationExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, ConcurrentDictionary<string, string> defaultsByKey)
    {
        TranslationSiteResult? result = TranslationSiteDetector.DetectAt(
            context.SemanticModel,
            context.Node,
            context.CancellationToken);
        if (result is null)
        {
            return;
        }

        foreach (DetectionProblem problem in result.Problems)
        {
            Report(context, problem);
        }

        if (result.Site is not null)
        {
            CheckDuplicate(context, result.Site, defaultsByKey);
        }
    }

    private static void Report(SyntaxNodeAnalysisContext context, DetectionProblem problem)
    {
        switch (problem.Cause)
        {
            case DetectionCause.NonConstantArgument:
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.NonConstant, problem.Location));
                break;
            case DetectionCause.InvalidMessageFormat:
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.InvalidMessage, problem.Location, problem.Detail));
                break;
            default:
                break;
        }
    }

    private static void CheckDuplicate(
        SyntaxNodeAnalysisContext context,
        TranslationSite site,
        ConcurrentDictionary<string, string> defaultsByKey)
    {
        var composite = TranslationKey.Compose(site.Key, site.Context);
        var firstDefault = defaultsByKey.GetOrAdd(composite, site.DefaultMessage);
        if (!string.Equals(firstDefault, site.DefaultMessage, StringComparison.Ordinal))
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.DuplicateKey, context.Node.GetLocation(), site.Key));
        }
    }
}
