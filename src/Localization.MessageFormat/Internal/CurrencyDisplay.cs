using System.Globalization;

namespace ArchPillar.Extensions.Localization.MessageFormat.Internal;

/// <summary>
/// Resolves a currency's display for a culture + width from the pinned CLDR data (<see cref="CurrencyData"/>),
/// walking the locale→language→root fallback chain. The single owner of "currency code + culture + width →
/// display", replacing the host-globalization <c>CurrencyLookup</c>.
/// </summary>
internal static class CurrencyDisplay
{
    public static int Digits(string code) => CurrencyData.Digits(code);

    public static string Spacing(CultureInfo culture) => CurrencyData.Spacing(culture.Name.ToLowerInvariant());

    public static IReadOnlyDictionary<string, string>? UnitPatterns(CultureInfo culture) =>
        CurrencyData.UnitPatterns(culture.Name.ToLowerInvariant());

    public static string Glyph(string code, CultureInfo culture, CurrencyWidth width)
    {
        if (width == CurrencyWidth.IsoCode)
        {
            return code.ToUpperInvariant();
        }

        var wantNarrow = width == CurrencyWidth.Narrow;
        foreach (var locale in Chain(culture))
        {
            if (!CurrencyData.TryEntry(locale, code, out CurrencyData.CurrencyEntry entry))
            {
                continue;
            }

            if (wantNarrow && entry.Narrow.Length > 0)
            {
                return entry.Narrow;
            }

            if (entry.Symbol.Length > 0)
            {
                return entry.Symbol;
            }
        }

        return code.ToUpperInvariant();
    }

    public static string Name(string code, CultureInfo culture, string pluralCategory)
    {
        foreach (var locale in Chain(culture))
        {
            if (CurrencyData.TryEntry(locale, code, out CurrencyData.CurrencyEntry entry)
                && entry.Names.Count > 0
                && (entry.Names.TryGetValue(pluralCategory, out var name) || entry.Names.TryGetValue("other", out name)))
            {
                return name;
            }
        }

        return code.ToUpperInvariant();
    }

    // CRITICAL: this MUST be the identical FIRST-DASH walk the generator/loader use (Parent/ResolveMeta):
    // "zh-Hant-HK" -> "zh" -> "root" (jump straight to the language, NOT segment-wise "zh-Hant" -> "zh").
    // The delta baselines were computed against this exact chain; a segment-wise truncation walk would
    // resolve three-part locales against the wrong baseline and return incorrect entries. Do not "fix" it.
    private static IEnumerable<string> Chain(CultureInfo culture)
    {
        var locale = culture.Name.ToLowerInvariant();
        while (true)
        {
            yield return locale;
            if (locale == "root")
            {
                yield break;
            }

            var dash = locale.IndexOf('-');
#if NETSTANDARD2_0
            locale = dash > 0 ? locale.Substring(0, dash) : "root";
#else
            locale = dash > 0 ? locale[..dash] : "root";
#endif
        }
    }
}
