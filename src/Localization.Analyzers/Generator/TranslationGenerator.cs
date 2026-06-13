using System.Collections.Immutable;
using ArchPillar.Extensions.Localization.Detection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ArchPillar.Extensions.Localization.Generator;

/// <summary>
/// The compile-time front end: it emits the strongly-typed key registry as source so call sites and the
/// analyzer share rename-safe keys. The on-disk source-language template is not produced here — the tool's
/// <c>extract</c> builds it from each built assembly's IL (Decision D-K), which also catches strings in
/// generated code (Razor/Blazor/MVC) that a syntax-level generator never sees.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class TranslationGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<ImmutableArray<TranslationSite?>> sites = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax or ElementAccessExpressionSyntax,
                static (syntaxContext, ct) =>
                    TranslationSiteDetector.DetectAt(syntaxContext.SemanticModel, syntaxContext.Node, includeStringLocalizer: true, ct)?.Site)
            .Where(static site => site is not null)
            .Collect();

        context.RegisterSourceOutput(
            sites,
            static (production, collected) =>
                production.AddSource("TranslationKeys.g.cs", TranslationKeyRegistryEmitter.Emit(collected)));
    }
}
