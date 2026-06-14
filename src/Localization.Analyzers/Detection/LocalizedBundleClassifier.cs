using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ArchPillar.Extensions.Localization.Detection;

/// <summary>
/// How a candidate type relates to the <c>Localized&lt;TSelf&gt;</c> bundle pattern — shared by the DI
/// registration generator and the "mark partial" analyzer so both agree exactly.
/// </summary>
internal readonly struct LocalizedBundle
{
    public LocalizedBundle(bool isBundle, bool isRecord, bool isPartial, bool hasInjectableConstructor, bool hasUserConstructor)
    {
        IsBundle = isBundle;
        IsRecord = isRecord;
        IsPartial = isPartial;
        HasInjectableConstructor = hasInjectableConstructor;
        HasUserConstructor = hasUserConstructor;
    }

    /// <summary>A concrete, top-level, non-generic class deriving from <c>Localized&lt;TSelf&gt;</c>.</summary>
    public bool IsBundle { get; }

    /// <summary>The bundle is a record — registrable, but excluded from constructor generation.</summary>
    public bool IsRecord { get; }

    /// <summary>The bundle is declared <c>partial</c> (on any of its declarations).</summary>
    public bool IsPartial { get; }

    /// <summary>The bundle declares an accessible <c>ILocalizer&lt;TSelf&gt;</c> constructor.</summary>
    public bool HasInjectableConstructor { get; }

    /// <summary>The bundle declares any constructor of its own (an implicit default does not count).</summary>
    public bool HasUserConstructor { get; }

    /// <summary>The generator will synthesize the constructors for a partial bundle that declares none.</summary>
    public bool WillGenerateConstructor => IsBundle && IsPartial && !IsRecord && !HasUserConstructor;

    /// <summary>Registrable in DI once it has — or will be generated — an <c>ILocalizer&lt;TSelf&gt;</c> constructor.</summary>
    public bool IsRegistrable => IsBundle && (HasInjectableConstructor || WillGenerateConstructor);

    /// <summary>A non-partial, constructor-less bundle that going <c>partial</c> would make injectable.</summary>
    public bool ShouldSuggestPartial => IsBundle && !IsRecord && !IsPartial && !HasInjectableConstructor && !HasUserConstructor;
}

/// <summary>Classifies a type against the <c>Localized&lt;TSelf&gt;</c> bundle pattern.</summary>
internal static class LocalizedBundleClassifier
{
    public static LocalizedBundle Classify(INamedTypeSymbol type, Compilation compilation)
    {
        INamedTypeSymbol? localizedBase = compilation.GetTypeByMetadataName("ArchPillar.Extensions.Localization.Localized`1");
        INamedTypeSymbol? localizer = compilation.GetTypeByMetadataName("ArchPillar.Extensions.Localization.ILocalizer`1");
        if (localizedBase is null || localizer is null)
        {
            return default;
        }

        return Classify(type, localizedBase, localizer);
    }

    public static LocalizedBundle Classify(INamedTypeSymbol type, INamedTypeSymbol localizedBase, INamedTypeSymbol localizer)
    {
        if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsStatic || type.IsGenericType || type.ContainingType is not null)
        {
            return default;
        }

        if (!DerivesFrom(type, localizedBase))
        {
            return default;
        }

        var hasUserConstructor = type.InstanceConstructors.Any(constructor => !constructor.IsImplicitlyDeclared);
        return new LocalizedBundle(
            isBundle: true,
            type.IsRecord,
            IsDeclaredPartial(type),
            HasInjectableConstructor(type, localizer),
            hasUserConstructor);
    }

    private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol localizedBase)
    {
        for (INamedTypeSymbol? current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, localizedBase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasInjectableConstructor(INamedTypeSymbol type, INamedTypeSymbol localizer)
    {
        foreach (IMethodSymbol constructor in type.InstanceConstructors)
        {
            if (constructor.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
            {
                continue;
            }

            if (constructor.Parameters.Length == 1 &&
                constructor.Parameters[0].Type is INamedTypeSymbol parameterType &&
                SymbolEqualityComparer.Default.Equals(parameterType.OriginalDefinition, localizer) &&
                parameterType.TypeArguments.Length == 1 &&
                SymbolEqualityComparer.Default.Equals(parameterType.TypeArguments[0], type))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDeclaredPartial(INamedTypeSymbol type)
    {
        foreach (SyntaxReference reference in type.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is TypeDeclarationSyntax declaration &&
                declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                return true;
            }
        }

        return false;
    }
}
