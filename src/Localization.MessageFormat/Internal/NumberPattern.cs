namespace ArchPillar.Extensions.Localization.MessageFormat.Internal;

/// <summary>The kind of one token in a parsed CLDR pattern affix.</summary>
internal enum PatternTokenKind
{
    /// <summary>Literal text emitted exactly as stored (including no-break spaces).</summary>
    Literal,

    /// <summary>The currency symbol placeholder (<c>¤</c>).</summary>
    CurrencySymbol,

    /// <summary>The ISO currency code placeholder (<c>¤¤</c>).</summary>
    CurrencyCode,

    /// <summary>The locale percent sign (<c>%</c>); its presence also scales the value by 100.</summary>
    PercentSign,

    /// <summary>The locale minus sign (<c>-</c>).</summary>
    MinusSign
}

/// <summary>One token of a parsed pattern affix.</summary>
/// <param name="Kind">The token kind.</param>
/// <param name="Text">The literal text for <see cref="PatternTokenKind.Literal"/>; empty otherwise.</param>
internal readonly record struct PatternToken(PatternTokenKind Kind, string Text);

/// <summary>
/// A parsed CLDR number pattern: the affix token sequences around the digit body, and the digit bounds
/// the body declares. Negative affixes are <see langword="null"/> when the pattern has no negative
/// subpattern — the renderer then derives them (locale minus sign + the positive affixes), per CLDR.
/// Grouping positions are deliberately not modeled: they are an atom applied from
/// <see cref="System.Globalization.NumberFormatInfo.NumberGroupSizes"/> (spec E2/E4).
/// </summary>
/// <param name="PositivePrefix">Tokens before the digits for non-negative values.</param>
/// <param name="PositiveSuffix">Tokens after the digits for non-negative values.</param>
/// <param name="NegativePrefix">Tokens before the digits for negative values, or <see langword="null"/>.</param>
/// <param name="NegativeSuffix">Tokens after the digits for negative values, or <see langword="null"/>.</param>
/// <param name="MinIntegerDigits">The minimum integer digits (count of <c>0</c> in the integer body).</param>
/// <param name="MinFractionDigits">The minimum fraction digits (count of <c>0</c> in the fraction body).</param>
/// <param name="MaxFractionDigits">The maximum fraction digits (count of <c>0</c> and <c>#</c> in the fraction body).</param>
internal sealed record NumberPattern(
    IReadOnlyList<PatternToken> PositivePrefix,
    IReadOnlyList<PatternToken> PositiveSuffix,
    IReadOnlyList<PatternToken>? NegativePrefix,
    IReadOnlyList<PatternToken>? NegativeSuffix,
    int MinIntegerDigits,
    int MinFractionDigits,
    int MaxFractionDigits)
{
    /// <summary>Whether an affix carries the percent sign (the renderer then scales the value by 100).</summary>
    public bool IsPercent => HasPercent(PositivePrefix) || HasPercent(PositiveSuffix);

    private static bool HasPercent(IReadOnlyList<PatternToken> tokens)
    {
        foreach (PatternToken token in tokens)
        {
            if (token.Kind == PatternTokenKind.PercentSign)
            {
                return true;
            }
        }

        return false;
    }
}
