namespace ArchPillar.Extensions.Localization.MessageFormat.Internal;

/// <summary>The CLDR standard number patterns for one locale.</summary>
/// <param name="Decimal">The standard decimal pattern (for example <c>#,##0.###</c>).</param>
/// <param name="Percent">The standard percent pattern (for example <c>#,##0 %</c>).</param>
/// <param name="Currency">The standard currency pattern (for example <c>#,##0.00 ¤</c>).</param>
internal sealed record CldrNumberPatternSet(string Decimal, string Percent, string Currency);
