using Microsoft.CodeAnalysis;

namespace ArchPillar.Extensions.Localization.Detection;

/// <summary>
/// A recognized translatable call site: the constants bound to the attributed parameters and the
/// placeholders parsed from the default message. This is the format-neutral extraction output shared by
/// the analyzer, the generator, and the tool.
/// </summary>
/// <param name="Key">The stable symbolic key.</param>
/// <param name="DefaultMessage">The in-code source default (ICU MessageFormat).</param>
/// <param name="Context">The optional disambiguation context.</param>
/// <param name="Comment">The optional translator comment.</param>
/// <param name="Placeholders">The argument names the default message references.</param>
/// <param name="Reference">The source location of the call.</param>
public sealed record TranslationSite(
    string Key,
    string DefaultMessage,
    string? Context,
    string? Comment,
    IReadOnlyList<string> Placeholders,
    SourceReference Reference);

/// <summary>
/// The shared cause enumeration that the analyzer maps to diagnostics and the extractor maps to build
/// problems, so both produce identical results for the same code.
/// </summary>
public enum DetectionCause
{
    /// <summary>An attributed argument is not a compile-time constant string.</summary>
    NonConstantArgument,

    /// <summary>The default message is not valid ICU MessageFormat syntax.</summary>
    InvalidMessageFormat
}

/// <summary>
/// A problem found at a translation site, carrying its cause and source location.
/// </summary>
/// <param name="Cause">The cause of the problem.</param>
/// <param name="Detail">Optional human-readable detail.</param>
/// <param name="Location">The source location to flag.</param>
public sealed record DetectionProblem(DetectionCause Cause, string? Detail, Location Location);

/// <summary>
/// The result of inspecting one call site: a successful <see cref="TranslationSite"/> when extractable,
/// plus any problems found.
/// </summary>
/// <param name="Site">The extracted site, or <see langword="null"/> when it could not be built.</param>
/// <param name="Problems">The problems found at the site.</param>
public sealed record TranslationSiteResult(TranslationSite? Site, IReadOnlyList<DetectionProblem> Problems);
