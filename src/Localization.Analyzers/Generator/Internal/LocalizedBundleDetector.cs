using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ArchPillar.Extensions.Localization.Generator.Internal;

/// <summary>
/// Recognises a concrete <c>Localized&lt;TSelf&gt;</c> subclass that can be injected — one with an accessible
/// constructor taking <c>ILocalizer&lt;TSelf&gt;</c> — and returns its fully-qualified name for the generated
/// DI registration. A bundle with only the ambient (parameterless) constructor is ambient-only and is skipped,
/// since it cannot be resolved through the container under a static-free configuration.
/// </summary>
internal static class LocalizedBundleDetector
{
    public static string? DetectAt(SemanticModel semanticModel, SyntaxNode node, CancellationToken cancellationToken)
    {
        if (node is not TypeDeclarationSyntax declaration)
        {
            return null;
        }

        if (semanticModel.GetDeclaredSymbol(declaration, cancellationToken) is not INamedTypeSymbol bundle)
        {
            return null;
        }

        if (bundle.IsAbstract || bundle.IsStatic)
        {
            return null;
        }

        INamedTypeSymbol? localizedBase = semanticModel.Compilation.GetTypeByMetadataName("ArchPillar.Extensions.Localization.Localized`1");
        INamedTypeSymbol? localizer = semanticModel.Compilation.GetTypeByMetadataName("ArchPillar.Extensions.Localization.ILocalizer`1");
        if (localizedBase is null || localizer is null)
        {
            return null;
        }

        if (!DerivesFrom(bundle, localizedBase) || !HasInjectableConstructor(bundle, localizer))
        {
            return null;
        }

        return bundle.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static bool DerivesFrom(INamedTypeSymbol bundle, INamedTypeSymbol localizedBase)
    {
        for (INamedTypeSymbol? type = bundle.BaseType; type is not null; type = type.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, localizedBase))
            {
                return true;
            }
        }

        return false;
    }

    // An accessible (public or internal) constructor taking exactly ILocalizer<TSelf> — the injection point the
    // generated factory resolves and passes. This is the constructor a DI bundle declares as `: base(loc)`.
    private static bool HasInjectableConstructor(INamedTypeSymbol bundle, INamedTypeSymbol localizer)
    {
        foreach (IMethodSymbol constructor in bundle.InstanceConstructors)
        {
            if (constructor.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
            {
                continue;
            }

            if (constructor.Parameters.Length == 1 &&
                constructor.Parameters[0].Type is INamedTypeSymbol parameterType &&
                SymbolEqualityComparer.Default.Equals(parameterType.OriginalDefinition, localizer) &&
                parameterType.TypeArguments.Length == 1 &&
                SymbolEqualityComparer.Default.Equals(parameterType.TypeArguments[0], bundle))
            {
                return true;
            }
        }

        return false;
    }
}
