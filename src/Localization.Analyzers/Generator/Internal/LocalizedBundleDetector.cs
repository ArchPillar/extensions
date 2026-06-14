using ArchPillar.Extensions.Localization.Detection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ArchPillar.Extensions.Localization.Generator.Internal;

/// <summary>What the generator needs to emit for one <c>Localized&lt;TSelf&gt;</c> bundle.</summary>
internal sealed record LocalizedBundleEmit(
    string FullyQualifiedName,
    string? Namespace,
    string TypeName,
    bool Register,
    bool GenerateConstructor);

/// <summary>
/// Maps a <c>Localized&lt;TSelf&gt;</c> bundle declaration to its emit plan — whether to register it for DI and
/// whether to synthesize its constructors — via the shared <see cref="LocalizedBundleClassifier"/>.
/// </summary>
internal static class LocalizedBundleDetector
{
    public static LocalizedBundleEmit? DetectAt(SemanticModel semanticModel, SyntaxNode node, CancellationToken cancellationToken)
    {
        if (node is not TypeDeclarationSyntax declaration)
        {
            return null;
        }

        if (semanticModel.GetDeclaredSymbol(declaration, cancellationToken) is not INamedTypeSymbol bundle)
        {
            return null;
        }

        LocalizedBundle classification = LocalizedBundleClassifier.Classify(bundle, semanticModel.Compilation);
        if (!classification.IsRegistrable && !classification.WillGenerateConstructor)
        {
            return null;
        }

        return new LocalizedBundleEmit(
            bundle.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            bundle.ContainingNamespace.IsGlobalNamespace ? null : bundle.ContainingNamespace.ToDisplayString(),
            bundle.Name,
            classification.IsRegistrable,
            classification.WillGenerateConstructor);
    }
}
