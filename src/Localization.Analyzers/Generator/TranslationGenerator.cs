using System.Collections.Immutable;
using ArchPillar.Extensions.Localization.Detection;
using ArchPillar.Extensions.Localization.Generator.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ArchPillar.Extensions.Localization.Generator;

/// <summary>
/// The compile-time front end: it emits the strongly-typed key registry as source so call sites and the
/// analyzer share rename-safe keys, and an <c>internal</c> DI registration for the <c>Localized&lt;T&gt;</c>
/// bundles in the assembly. The on-disk source-language template is not produced here — the tool's
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
                static (node, _) => node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax or ElementAccessExpressionSyntax or ElementBindingExpressionSyntax,
                static (syntaxContext, ct) =>
                    TranslationSiteDetector.DetectAt(syntaxContext.SemanticModel, syntaxContext.Node, includeStringLocalizer: true, ct)?.Site)
            .Where(static site => site is not null)
            .Collect();

        context.RegisterSourceOutput(
            sites,
            static (production, collected) =>
                production.AddSource("TranslationKeys.g.cs", TranslationKeyRegistryEmitter.Emit(collected)));

        // The Localized<T> bundles in this assembly, paired with whether the DI abstractions are referenced —
        // the registration is emitted only when the consumer actually uses dependency injection.
        IncrementalValueProvider<ImmutableArray<string?>> bundles = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null } or RecordDeclarationSyntax { BaseList: not null },
                static (syntaxContext, ct) => LocalizedBundleDetector.DetectAt(syntaxContext.SemanticModel, syntaxContext.Node, ct))
            .Where(static name => name is not null)
            .Collect();

        IncrementalValueProvider<bool> dependencyInjectionReferenced = context.CompilationProvider
            .Select(static (compilation, _) =>
                compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.IServiceCollection") is not null);

        context.RegisterSourceOutput(
            bundles.Combine(dependencyInjectionReferenced),
            static (production, pair) =>
            {
                if (!pair.Right)
                {
                    return;
                }

                var source = LocalizedBundleRegistrationEmitter.Emit(pair.Left);
                if (source is not null)
                {
                    production.AddSource("LocalizedBundleRegistration.g.cs", source);
                }
            });
    }
}
