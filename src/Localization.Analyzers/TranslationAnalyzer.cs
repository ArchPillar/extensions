using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
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
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        Diagnostics.NonConstant,
        Diagnostics.InvalidMessage,
        Diagnostics.PlaceholderNotSupplied,
        Diagnostics.ArgumentNotUsed,
        Diagnostics.MissingOther,
        Diagnostics.DuplicateKey,
        Diagnostics.IdenticalText,
        Diagnostics.KeyPattern);

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

        var state = new AnalysisState(KeyPatternOf(context.Options));
        context.RegisterSyntaxNodeAction(
            nodeContext => Analyze(nodeContext, state),
            SyntaxKind.InvocationExpression,
            SyntaxKind.ObjectCreationExpression,
            SyntaxKind.ImplicitObjectCreationExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, AnalysisState state)
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
            CheckSite(context, result.Site, state);
        }
    }

    private static void Report(SyntaxNodeAnalysisContext context, DetectionProblem problem)
    {
        DiagnosticDescriptor? descriptor = problem.Cause switch
        {
            DetectionCause.NonConstantArgument => Diagnostics.NonConstant,
            DetectionCause.InvalidMessageFormat => Diagnostics.InvalidMessage,
            DetectionCause.PlaceholderNotSupplied => Diagnostics.PlaceholderNotSupplied,
            DetectionCause.ArgumentNotUsed => Diagnostics.ArgumentNotUsed,
            DetectionCause.MissingOtherBranch => Diagnostics.MissingOther,
            _ => null
        };

        if (descriptor is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(descriptor, problem.Location, problem.Detail));
        }
    }

    private static void CheckSite(SyntaxNodeAnalysisContext context, TranslationSite site, AnalysisState state)
    {
        Location location = context.Node.GetLocation();
        var composite = TranslationKey.Compose(site.Key, site.Context);

        var firstDefault = state.DefaultsByKey.GetOrAdd(composite, site.DefaultMessage);
        if (!string.Equals(firstDefault, site.DefaultMessage, StringComparison.Ordinal))
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.DuplicateKey, location, site.Key));
        }

        var textKey = site.DefaultMessage + TranslationKey.Separator + (site.Context ?? string.Empty);
        var firstKey = state.KeysByDefault.GetOrAdd(textKey, site.Key);
        if (!string.Equals(firstKey, site.Key, StringComparison.Ordinal))
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.IdenticalText, location, firstKey));
        }

        if (state.KeyPattern?.IsMatch(site.Key) == false)
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.KeyPattern, location, site.Key, state.KeyPattern.ToString()));
        }
    }

    private static Regex? KeyPatternOf(AnalyzerOptions options)
    {
        if (options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.ArchPillarLocalizationKeyPattern", out var pattern)
            && pattern.Length > 0)
        {
            return new Regex(pattern, RegexOptions.CultureInvariant);
        }

        return null;
    }

    private sealed class AnalysisState
    {
        public AnalysisState(Regex? keyPattern)
        {
            KeyPattern = keyPattern;
        }

        public Regex? KeyPattern { get; }

        public ConcurrentDictionary<string, string> DefaultsByKey { get; } = new(StringComparer.Ordinal);

        public ConcurrentDictionary<string, string> KeysByDefault { get; } = new(StringComparer.Ordinal);
    }
}
