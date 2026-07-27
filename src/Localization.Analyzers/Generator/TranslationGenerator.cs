using System.Collections.Immutable;
using ArchPillar.Extensions.Localization.Generator.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ArchPillar.Extensions.Localization.Generator;

/// <summary>
/// The compile-time front end: it synthesizes constructors for the <c>Localized&lt;T&gt;</c> bundles in the
/// assembly and an <c>internal</c> DI registration for them. The on-disk source-language template is not
/// produced here — the tool's <c>extract</c> builds it from each built assembly's IL (Decision D-K), which
/// also catches strings in generated code (Razor/Blazor/MVC) that a syntax-level generator never sees.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class TranslationGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // The Localized<T> bundles in this assembly: each carries whether to register it for DI and whether the
        // generator should synthesize its constructors.
        IncrementalValueProvider<ImmutableArray<LocalizedBundleEmit?>> bundles = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null } or RecordDeclarationSyntax { BaseList: not null },
                static (syntaxContext, cancellationToken) => LocalizedBundleDetector.DetectAt(syntaxContext.SemanticModel, syntaxContext.Node, cancellationToken))
            .Where(static bundle => bundle is not null)
            .Collect();

        // Constructors for partial bundles that declare none — not gated on DI, so the ambient `new` form works too.
        context.RegisterSourceOutput(
            bundles,
            static (production, collected) =>
            {
                var source = LocalizedBundleConstructorEmitter.Emit(collected);
                if (source is not null)
                {
                    production.AddSource("LocalizedBundleConstructors.g.cs", source);
                }
            });

        // The DI registration is emitted only when the consumer actually references the DI abstractions.
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
