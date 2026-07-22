using System.Globalization;
using System.Text;

namespace ArchPillar.Extensions.Localization.MessageFormat.Internal;

/// <summary>
/// Digit precision for <see cref="PatternRenderer"/>: fraction-digit bounds (standard formats) or
/// significant digits (reserved for a future significant-digit skeleton such as <c>::@@#</c>; compact
/// notation uses fraction precision).
/// </summary>
internal readonly record struct PatternPrecision
{
    private PatternPrecision(int minFraction, int maxFraction, int significantDigits)
    {
        MinFraction = minFraction;
        MaxFraction = maxFraction;
        SignificantDigits = significantDigits;
    }

    /// <summary>The minimum fraction digits (fraction mode).</summary>
    public int MinFraction { get; }

    /// <summary>The maximum fraction digits (fraction mode).</summary>
    public int MaxFraction { get; }

    /// <summary>The significant-digit count; positive only in significant mode.</summary>
    public int SignificantDigits { get; }

    /// <summary>Whether this precision is significant-digit based.</summary>
    public bool IsSignificant => SignificantDigits > 0;

    /// <summary>Fraction-digit precision: at least <paramref name="min"/>, at most <paramref name="max"/> digits.</summary>
    public static PatternPrecision Fraction(int min, int max) => new(min, max, 0);

    /// <summary>Significant-digit precision: round to <paramref name="digits"/> significant digits, trimmed. Reserved: no current skeleton selects significant-digit precision.</summary>
    public static PatternPrecision Significant(int digits) => new(0, 0, digits);
}

/// <summary>
/// Applies a parsed CLDR <see cref="NumberPattern"/> to a value: formats the digits through the culture's
/// atoms (.NET custom format string — separators and grouping positions from <see cref="NumberFormatInfo"/>)
/// and composes the pattern's affixes around them, substituting the currency symbol/code, the locale percent
/// and minus signs, and emitting literal text (including no-break spaces) exactly as the data carries it.
/// The applicator is pattern-source-agnostic — Spec 3's compact formatter calls it with bucket patterns.
/// </summary>
internal static class PatternRenderer
{
    // decimal's maximum scale; used as the open-ended fraction bound in significant mode (trailing '#'s trim).
    // Internal (not private): NumberSkeleton reads it too, so both the significant-digit clamp here and the
    // parse-time fraction-stem rejection there share the one source of truth for decimal's rounding limit.
    internal const int MaxDecimalScale = 28;

    // The largest absolute value that can scale by 100 (percent rendering) without overflowing decimal range.
    private static readonly decimal _maxPercentValue = decimal.MaxValue / 100m;

    public static string Render(
        NumberPattern pattern,
        decimal value,
        PatternPrecision precision,
        bool grouping,
        CultureInfo culture,
        string currencySymbol,
        string currencyCode,
        string currencySpacingInsert)
    {
        var negative = value < 0m;
        var absolute = Math.Abs(value);
        if (pattern.IsPercent)
        {
            if (absolute > _maxPercentValue)
            {
                throw new MessageFormatException(
                    $"Value {value} cannot be rendered as a percent: scaling by 100 would overflow decimal range.", -1);
            }

            absolute *= 100m;
        }

        if (precision.IsSignificant)
        {
            absolute = RoundToSignificant(absolute, precision.SignificantDigits);
        }

        var digits = absolute.ToString(DigitFormat(pattern, precision, grouping), culture);

        IReadOnlyList<PatternToken> prefix;
        IReadOnlyList<PatternToken> suffix;
        var deriveMinus = false;
        if (negative && pattern.NegativePrefix is not null)
        {
            prefix = pattern.NegativePrefix;
            suffix = pattern.NegativeSuffix!;
        }
        else
        {
            // No negative subpattern: CLDR derives the negative form as minus sign + positive affixes.
            prefix = pattern.PositivePrefix;
            suffix = pattern.PositiveSuffix;
            deriveMinus = negative;
        }

        var builder = new StringBuilder();
        if (deriveMinus)
        {
            builder.Append(culture.NumberFormat.NegativeSign);
        }

        var prefixGlyph = NumberAdjacentGlyph(prefix, last: true, currencySymbol, currencyCode);
        var suffixGlyph = NumberAdjacentGlyph(suffix, last: false, currencySymbol, currencyCode);

        AppendTokens(builder, prefix, culture, currencySymbol, currencyCode);
        if (digits.Length > 0 && NeedsSpacing(currencySpacingInsert, prefixGlyph, digits[0], glyphIsPrefix: true))
        {
            builder.Append(currencySpacingInsert);
        }

        builder.Append(digits);

        if (digits.Length > 0)
        {
#if NETSTANDARD2_0
            var lastDigit = digits[digits.Length - 1];
#else
            var lastDigit = digits[^1];
#endif
            if (NeedsSpacing(currencySpacingInsert, suffixGlyph, lastDigit, glyphIsPrefix: false))
            {
                builder.Append(currencySpacingInsert);
            }
        }

        AppendTokens(builder, suffix, culture, currencySymbol, currencyCode);
        return builder.ToString();
    }

    // The glyph substituted for the currency token that sits adjacent to the number: the LAST token of a
    // prefix, or the FIRST token of a suffix. Returns "" when that adjacent token is not the currency glyph
    // (e.g. a trailing literal space in the affix), so no spacing is applied.
    private static string NumberAdjacentGlyph(IReadOnlyList<PatternToken> tokens, bool last, string symbol, string code)
    {
        if (tokens.Count == 0)
        {
            return string.Empty;
        }

        PatternToken token = last ? tokens[tokens.Count - 1] : tokens[0];
        return token.Kind switch
        {
            PatternTokenKind.CurrencySymbol => symbol,
            PatternTokenKind.CurrencyCode => code,
            _ => string.Empty
        };
    }

    // CLDR currencySpacing: insert when the glyph's number-facing edge matches currencyMatch [[:^S:]&[:^Z:]]
    // (not a Symbol and not a Separator) and the number edge is a digit (surroundingMatch [:digit:]).
    // The number-facing edge is unambiguous per side: a PREFIX glyph's is its LAST char (the number sits to
    // its right); a SUFFIX glyph's is its FIRST char (the number sits to its left).
    private static bool NeedsSpacing(string insert, string glyph, char numberEdge, bool glyphIsPrefix)
    {
        if (insert.Length == 0 || glyph.Length == 0 || !char.IsDigit(numberEdge))
        {
            return false;
        }

#if NETSTANDARD2_0
        var glyphEdge = glyphIsPrefix ? glyph[glyph.Length - 1] : glyph[0];
#else
        var glyphEdge = glyphIsPrefix ? glyph[^1] : glyph[0];
#endif
        return NonSymbolNonSeparator(glyphEdge);
    }

    private static bool NonSymbolNonSeparator(char c)
    {
        UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
        return category is not (UnicodeCategory.CurrencySymbol or UnicodeCategory.MathSymbol
            or UnicodeCategory.ModifierSymbol or UnicodeCategory.OtherSymbol
            or UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator
            or UnicodeCategory.ParagraphSeparator);
    }

    /// <summary>Rounds to <paramref name="digits"/> significant digits, half away from zero (ECMA-402's default). Reserved for a future significant-digit skeleton; unused by compact notation, which rounds by fraction digits.</summary>
    internal static decimal RoundToSignificant(decimal value, int digits)
    {
        if (value == 0m)
        {
            return 0m;
        }

        var magnitude = (int)Math.Floor(Math.Log10((double)Math.Abs(value)));
        var scale = digits - 1 - magnitude;
        if (scale >= 0)
        {
            return Math.Round(value, Math.Min(scale, MaxDecimalScale), MidpointRounding.AwayFromZero);
        }

        var factor = Pow10(-scale);
        return Math.Round(value / factor, 0, MidpointRounding.AwayFromZero) * factor;
    }

    private static string DigitFormat(NumberPattern pattern, PatternPrecision precision, bool grouping)
    {
        var builder = new StringBuilder(grouping ? "#,##0" : "0");
        if (pattern.MinIntegerDigits > 1)
        {
            builder.Append('0', pattern.MinIntegerDigits - 1);
        }

        var min = precision.IsSignificant ? 0 : precision.MinFraction;
        var max = precision.IsSignificant ? MaxDecimalScale : precision.MaxFraction;
        if (max > 0)
        {
            builder.Append('.').Append('0', min).Append('#', max - min);
        }

        return builder.ToString();
    }

    private static void AppendTokens(
        StringBuilder builder,
        IReadOnlyList<PatternToken> tokens,
        CultureInfo culture,
        string currencySymbol,
        string currencyCode)
    {
        foreach (PatternToken token in tokens)
        {
            switch (token.Kind)
            {
                case PatternTokenKind.Literal:
                    builder.Append(token.Text);
                    break;
                case PatternTokenKind.CurrencySymbol:
                    builder.Append(currencySymbol);
                    break;
                case PatternTokenKind.CurrencyCode:
                    builder.Append(currencyCode);
                    break;
                case PatternTokenKind.PercentSign:
                    builder.Append(culture.NumberFormat.PercentSymbol);
                    break;
                case PatternTokenKind.MinusSign:
                    builder.Append(culture.NumberFormat.NegativeSign);
                    break;
                default:
                    break;
            }
        }
    }

    private static decimal Pow10(int exponent)
    {
        var result = 1m;
        for (var index = 0; index < exponent; index++)
        {
            result *= 10m;
        }

        return result;
    }
}
