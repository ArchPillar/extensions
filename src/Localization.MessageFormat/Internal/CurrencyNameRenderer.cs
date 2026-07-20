using System.Globalization;

namespace ArchPillar.Extensions.Localization.MessageFormat.Internal;

/// <summary>
/// Composes ICU's <c>unit-width-full-name</c> currency form: the number formatted with the culture's
/// <em>decimal</em> pattern at the currency's fraction digits, wrapped in the locale's
/// <c>unitPattern-count-&lt;plural&gt;</c> around the plural-selected display name. Distinct from the
/// currency-pattern (¤) path — no symbol, no <c>currencySpacing</c>.
/// </summary>
internal static class CurrencyNameRenderer
{
    public static string Render(decimal value, string code, CultureInfo culture, int minFraction, int maxFraction, bool grouping)
    {
        // Round to the max fraction digits first, so the rendered number and the plural category selected on
        // it agree (mirrors CompactFormatter). AwayFromZero matches the pattern renderer.
        var rounded = Math.Round(value, maxFraction, MidpointRounding.AwayFromZero);
        NumberPattern decimalPattern = StandardPatterns.For(NumberUnit.Decimal, culture);
        var number = PatternRenderer.Render(
            decimalPattern, rounded, PatternPrecision.Fraction(minFraction, maxFraction), grouping, culture, string.Empty, string.Empty, string.Empty);

        // Plural category on the number AS RENDERED — the SAME selector the message/compact paths use
        // (PluralRules.Cardinal + PluralRules.Operands). visibleFraction = digits shown after the decimal
        // separator, so USD 1 -> "1.00" -> v=2 -> "other" (NOT "one"); JPY 1 -> "1" -> v=0 -> "one".
        var separator = culture.NumberFormat.NumberDecimalSeparator;
        var dot = number.IndexOf(separator, StringComparison.Ordinal);
        var visibleFraction = dot < 0 ? 0 : number.Length - dot - separator.Length;
        PluralCategory category = PluralRules.Cardinal(culture.Name, PluralRules.Operands(rounded, visibleFraction));
        var categoryKey = category.Keyword(); // CLDR count key ("one"/"other"/...)

        var name = CurrencyDisplay.Name(code, culture, categoryKey);

        IReadOnlyDictionary<string, string>? patterns = CurrencyDisplay.UnitPatterns(culture);
        var unitPattern = patterns is not null && (patterns.TryGetValue(categoryKey, out var p) || patterns.TryGetValue("other", out p))
            ? p
            : "{0} {1}";

        return unitPattern.Replace("{0}", number).Replace("{1}", name);
    }
}
