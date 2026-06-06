namespace ArchPillar.Extensions.Localization.MessageFormat;

/// <summary>
/// A structured parse error, carrying a human-readable message and the character offset at which
/// the problem was detected so a consumer (for example a Roslyn analyzer) can place a precise marker.
/// </summary>
/// <param name="Message">The human-readable description of the error.</param>
/// <param name="Position">The zero-based character offset into the source text where the error was detected.</param>
public sealed record MessageFormatError(string Message, int Position);
