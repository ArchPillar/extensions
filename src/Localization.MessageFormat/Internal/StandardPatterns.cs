using System.Globalization;

namespace ArchPillar.Extensions.Localization.MessageFormat.Internal;

/// <summary>
/// Resolves the CLDR standard number pattern for a unit in a culture, walking the locale fallback chain
/// (exact locale, then base language, then root). The single owner of "unit + culture -> standard
/// <see cref="NumberPattern"/>", shared by <see cref="NumberFormatting"/> (the standard render) and
/// <see cref="CompactFormatter"/> (the non-compacted plain render), so neither type depends on the other.
/// </summary>
internal static class StandardPatterns
{
    // The CLDR standard pattern for a unit in a culture: exact locale, then base language, then root —
    // mirroring PluralRules.RulesFor.
    public static NumberPattern For(NumberUnit unit, CultureInfo culture)
    {
        CldrNumberPatternSet set = PatternSetFor(culture.Name);
        var pattern = unit switch
        {
            NumberUnit.Percent => set.Percent,
            NumberUnit.Currency => set.Currency,
            _ => set.Decimal
        };
        return NumberPatternParser.Parse(pattern);
    }

    private static CldrNumberPatternSet PatternSetFor(string locale)
    {
        if (CldrNumberPatterns.Locales.TryGetValue(locale, out CldrNumberPatternSet? set))
        {
            return set;
        }

        var dash = locale.IndexOf('-');
        if (dash > 0)
        {
#if NETSTANDARD2_0
            var language = locale.Substring(0, dash);
#else
            var language = locale[..dash];
#endif
            if (CldrNumberPatterns.Locales.TryGetValue(language, out set))
            {
                return set;
            }
        }

        return CldrNumberPatterns.Locales["root"];
    }
}
