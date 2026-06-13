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

        Regex? keyPattern = KeyPatternOf(context.Options);
        var sites = new ConcurrentBag<RecordedSite>();
        context.RegisterSyntaxNodeAction(
            nodeContext => Analyze(nodeContext, keyPattern, sites),
            SyntaxKind.InvocationExpression,
            SyntaxKind.ObjectCreationExpression,
            SyntaxKind.ImplicitObjectCreationExpression,
            SyntaxKind.ElementAccessExpression);

        // Duplicate-key (APL0006) and identical-text (APL0007) need the whole compilation: deciding them
        // per node made the result depend on analysis order and blind to cross-file pairs in the IDE. They
        // are computed once, deterministically, after every site has been collected.
        context.RegisterCompilationEndAction(endContext => ReportCrossSiteConflicts(endContext, sites));
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, Regex? keyPattern, ConcurrentBag<RecordedSite> sites)
    {
        // The editor analyzer recognises our own attribute-annotated indexer (so a non-constant key or bad ICU
        // is flagged there too) but suppresses the BCL IStringLocalizer indexer, which is extraction-only.
        TranslationSiteResult? result = TranslationSiteDetector.DetectAt(
            context.SemanticModel,
            context.Node,
            includeStringLocalizer: false,
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
            CheckSite(context, result.Site, keyPattern, sites);
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

    private static void CheckSite(SyntaxNodeAnalysisContext context, TranslationSite site, Regex? keyPattern, ConcurrentBag<RecordedSite> sites)
    {
        Location location = context.Node.GetLocation();

        // The key pattern is a per-site rule with no cross-site state, so it stays on the node action.
        if (keyPattern is not null && !Matches(keyPattern, site.Key))
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.KeyPattern, location, site.Key, keyPattern.ToString()));
        }

        // Duplicate-key and identical-text are decided over the whole compilation, so record the site for the
        // end action. Identity is scoped by category: the same key under two categories is a different string.
        var composite = site.Category + TranslationKey.Separator + TranslationKey.Compose(site.Key, site.Context);
        var textKey = site.Category + TranslationKey.Separator + site.DefaultMessage + TranslationKey.Separator + (site.Context ?? string.Empty);
        sites.Add(new RecordedSite(composite, textKey, site.Key, site.DefaultMessage, location));
    }

    // Reports the conflicts that need a whole-compilation view, deterministically: within each conflicting
    // group the earliest site (by file then position) is the canonical one and the others are flagged, so the
    // result does not depend on the order nodes were analyzed or on which file the IDE happened to open.
    private static void ReportCrossSiteConflicts(CompilationAnalysisContext context, ConcurrentBag<RecordedSite> sites)
    {
        RecordedSite[] all = [.. sites];

        // APL0006 — one (category, key, context) bound to more than one default message.
        foreach (IGrouping<string, RecordedSite> group in all.GroupBy(site => site.Composite, StringComparer.Ordinal))
        {
            if (group.Select(site => site.DefaultMessage).Distinct(StringComparer.Ordinal).Take(2).Count() < 2)
            {
                continue;
            }

            RecordedSite canonical = Canonical(group);
            foreach (RecordedSite site in group)
            {
                if (!string.Equals(site.DefaultMessage, canonical.DefaultMessage, StringComparison.Ordinal))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Diagnostics.DuplicateKey, site.Location, site.Key));
                }
            }
        }

        // APL0007 — identical (category, default text, context) used under more than one key.
        foreach (IGrouping<string, RecordedSite> group in all.GroupBy(site => site.TextKey, StringComparer.Ordinal))
        {
            if (group.Select(site => site.Key).Distinct(StringComparer.Ordinal).Take(2).Count() < 2)
            {
                continue;
            }

            RecordedSite canonical = Canonical(group);
            foreach (RecordedSite site in group)
            {
                if (!string.Equals(site.Key, canonical.Key, StringComparison.Ordinal))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Diagnostics.IdenticalText, site.Location, canonical.Key));
                }
            }
        }
    }

    private static RecordedSite Canonical(IEnumerable<RecordedSite> group) =>
        group.OrderBy(site => LocationOrder(site.Location), StringComparer.Ordinal).First();

    private static string LocationOrder(Location location)
    {
        FileLinePositionSpan span = location.GetLineSpan();
        return $"{span.Path}:{span.StartLinePosition.Line:D8}:{span.StartLinePosition.Character:D8}";
    }

    private static bool Matches(Regex pattern, string key)
    {
        try
        {
            return pattern.IsMatch(key);
        }
        catch (RegexMatchTimeoutException)
        {
            // A pathological pattern timed out on this key; accept the key rather than crash the analyzer.
            return true;
        }
    }

    private static Regex? KeyPatternOf(AnalyzerOptions options)
    {
        if (options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.ArchPillarLocalizationKeyPattern", out var pattern)
            && pattern.Length > 0)
        {
            try
            {
                // A user-authored pattern may be syntactically invalid (throwing at construction) or
                // pathological (hanging on match). A bounded match timeout caps the latter; catching the
                // former degrades to "no key-pattern check" instead of throwing out of CompilationStart,
                // which Roslyn would surface as AD0001 and which disables every APL diagnostic.
                return new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        return null;
    }

    private sealed class RecordedSite
    {
        public RecordedSite(string composite, string textKey, string key, string defaultMessage, Location location)
        {
            Composite = composite;
            TextKey = textKey;
            Key = key;
            DefaultMessage = defaultMessage;
            Location = location;
        }

        public string Composite { get; }

        public string TextKey { get; }

        public string Key { get; }

        public string DefaultMessage { get; }

        public Location Location { get; }
    }
}
