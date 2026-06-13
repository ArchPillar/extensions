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

    public static DiagnosticDescriptor PlaceholderNotSupplied { get; } = new(
        "APL0003",
        "Placeholder has no supplied argument",
        "Placeholder '{0}' has no matching argument supplied at the call site",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static DiagnosticDescriptor ArgumentNotUsed { get; } = new(
        "APL0004",
        "Argument is not used by the message",
        "Argument '{0}' is supplied but not used by the message",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static DiagnosticDescriptor MissingOther { get; } = new(
        "APL0005",
        "Plural or select is missing the 'other' branch",
        "The plural/select for '{0}' must include an 'other' branch",
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

    public static DiagnosticDescriptor IdenticalText { get; } = new(
        "APL0007",
        "Identical text under different keys",
        "The same default text is used under a different key than '{0}'; consider sharing a key",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static DiagnosticDescriptor KeyPattern { get; } = new(
        "APL0008",
        "Key does not match the required pattern",
        "Key '{0}' does not match the configured pattern '{1}'",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
