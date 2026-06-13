namespace ArchPillar.Extensions.Localization;

/// <summary>
/// A reference to a source location (file path and position) where a translatable call appears.
/// </summary>
/// <param name="FilePath">The path to the source file.</param>
/// <param name="Line">The one-based line number.</param>
/// <param name="Column">The one-based column number.</param>
public sealed record SourceReference(string FilePath, int Line, int Column);
