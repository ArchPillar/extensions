using Microsoft.CodeAnalysis;

namespace ArchPillar.Extensions.Localization.Analyzers;

/// <summary>The diagnostic descriptors surfaced by the localization analyzer (prefix <c>APL</c>).</summary>
internal static class Diagnostics
{
    private const string Category = "ArchPillar.Localization";

    public static DiagnosticDescriptor NonConstant { get; } = new(
        "APL0001",
        "Translatable argument must be a constant",
        "An argument to a translatable parameter must be a compile-time constant string so it can be extracted",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticDescriptor InvalidMessage { get; } = new(
        "APL0002",
        "Invalid ICU MessageFormat",
        "The default message is not valid ICU MessageFormat syntax: {0}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static DiagnosticDescriptor DuplicateKey { get; } = new(
        "APL0006",
        "Duplicate key with conflicting default",
        "Duplicate key '{0}' is used with conflicting default text",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
