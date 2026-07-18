using System.Collections.Concurrent;
using System.Globalization;

namespace ArchPillar.Extensions.Localization.MessageFormat.Internal;

/// <summary>
/// Resolves and applies a <c>{arg, number, X}</c> style. The style is either an ICU skeleton (<c>::…</c>,
/// parsed by <see cref="NumberSkeleton"/>), one of the named styles <c>integer</c>/<c>currency</c>/<c>percent</c>,
/// or absent (the default). Anything else is an error. Also computes the visible fraction-digit count the
/// default display would show, which drives plural operand selection so selection agrees with what is rendered.
/// </summary>
internal static class NumberFormatting
{
    // One parse per distinct skeleton: keys are template-authored style strings, so the cache is bounded
    // the same way the formatter's own template cache is (finite authored styles, never user input).
    private static readonly ConcurrentDictionary<string, NumberFormatSpec> _skeletons = new(StringComparer.Ordinal);

    /// <summary>Classifies and validates a style, throwing on an unknown or unsupported one.</summary>
    public static NumberFormatSpec Resolve(string? style)
    {
        if (string.IsNullOrEmpty(style))
        {
            return NumberFormatSpec.Default;
        }

        if (style!.StartsWith("::", StringComparison.Ordinal))
        {
            return _skeletons.GetOrAdd(style!, NumberSkeleton.Parse);
        }

        return style switch
        {
            "integer" => NumberFormatSpec.Integer,
            "currency" => NumberFormatSpec.Currency(null),
            "percent" => NumberFormatSpec.Percent,
            _ => throw new MessageFormatException($"Unknown number style '{style}'.", -1)
        };
    }

    /// <summary>Formats <paramref name="value"/> in <paramref name="culture"/> per <paramref name="style"/>.</summary>
    public static string Format(object? value, string? style, CultureInfo culture) =>
        FormatSpec(value, Resolve(style), culture);

    /// <summary>
    /// The number of fraction digits the default display (<c>#,##0.###</c>) shows for <paramref name="value"/>:
    /// rounded to three places with trailing zeros trimmed. Culture-independent (computed in the invariant
    /// culture), so it is the same count the renderer displays for <c>#</c>.
    /// </summary>
    public static int VisibleFractionDigits(decimal value)
    {
        var text = value.ToString("0.###", CultureInfo.InvariantCulture);
        var dot = text.IndexOf('.');
        return dot < 0 ? 0 : text.Length - dot - 1;
    }

    private static string FormatSpec(object? value, NumberFormatSpec spec, CultureInfo culture)
    {
        if (!TryToDecimal(value, out var number))
        {
            // Non-numeric or non-finite (NaN/±∞): fall back to the value's own culture rendering,
            // matching the pre-engine behavior ("NaN" and friends).
            return value is IFormattable formattable
                ? formattable.ToString(null, culture)
                : value?.ToString() ?? string.Empty;
        }

        NumberPattern pattern = PatternFor(spec.Unit, culture);
        var currencySymbol = string.Empty;
        var currencyCode = string.Empty;
        int minimum;
        int maximum;
        if (spec.Unit == NumberUnit.Currency)
        {
            int digits;
            if (spec.CurrencyCode is null)
            {
                // Named `currency` — the rendering culture's own currency.
                NumberFormatInfo info = culture.NumberFormat;
                currencySymbol = info.CurrencySymbol;
                digits = info.CurrencyDecimalDigits;
            }
            else
            {
                (currencySymbol, digits) = CurrencyLookup.Resolve(spec.CurrencyCode);
                currencyCode = spec.CurrencyCode;
            }

            // ICU's currency-digits override: minor units win over the pattern's fraction body.
            minimum = spec.MinFractionDigits ?? digits;
            maximum = spec.MaxFractionDigits ?? digits;
        }
        else
        {
            minimum = spec.MinFractionDigits ?? pattern.MinFractionDigits;
            maximum = spec.MaxFractionDigits ?? pattern.MaxFractionDigits;
        }

        if (maximum < minimum)
        {
            maximum = minimum;
        }

        return PatternRenderer.Render(
            pattern, number, PatternPrecision.Fraction(minimum, maximum), spec.Grouping, culture, currencySymbol, currencyCode);
    }

    // The CLDR standard pattern for a unit in a culture: exact locale, then base language, then root —
    // mirroring PluralRules.RulesFor.
    private static NumberPattern PatternFor(NumberUnit unit, CultureInfo culture)
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

    /// <summary>
    /// Converts a supplied argument to a finite <see cref="decimal"/>. NaN/±infinity and non-numeric
    /// values report <see langword="false"/>. The one owner of "argument → number" for both the plural
    /// path (<see cref="MessageRenderer"/>) and the formatting engine.
    /// </summary>
    internal static bool TryToDecimal(object? value, out decimal number)
    {
        switch (value)
        {
            case decimal d:
                number = d;
                return true;
            case double db:
                if (double.IsNaN(db) || double.IsInfinity(db))
                {
                    number = 0m;
                    return false;
                }

                number = (decimal)db;
                return true;
            case float fl:
                if (float.IsNaN(fl) || float.IsInfinity(fl))
                {
                    number = 0m;
                    return false;
                }

                number = (decimal)fl;
                return true;
            case null:
                number = 0m;
                return false;
            default:
                try
                {
                    number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                    return true;
                }
                catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
                {
                    number = 0m;
                    return false;
                }
        }
    }
}
