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
    /// rounded to three places with trailing zeros trimmed. The bound is hardcoded because every CLDR-48 decimal
    /// pattern is min-0/max-3 fraction digits (only grouping varies), so it matches the per-locale maximum the
    /// renderer applies for <c>#</c> — culture-independent, and guarded by a test over the generated pattern set.
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

        var currencySymbol = string.Empty;
        var currencyCode = string.Empty;
        var currencyDigits = 0;
        if (spec.Unit == NumberUnit.Currency)
        {
            var code = spec.CurrencyCode ?? CultureCurrencyCode(culture);
            if (code is null)
            {
                // No region (neutral culture, no explicit code): last-resort host symbol, unchanged legacy edge.
                NumberFormatInfo info = culture.NumberFormat;
                currencySymbol = info.CurrencySymbol;
                currencyCode = string.Empty;
                currencyDigits = info.CurrencyDecimalDigits;
            }
            else if (spec.Width == CurrencyWidth.FullName)
            {
                // Route the plural display-name form before touching the glyph/pattern path.
                (var minimumFraction, var maximumFraction) = ResolveCurrencyFraction(spec, CurrencyDisplay.Digits(code));
                return CurrencyNameRenderer.Render(number, code, culture, minimumFraction, maximumFraction, spec.Grouping);
            }
            else
            {
                currencySymbol = CurrencyDisplay.Glyph(code, culture, spec.Width);
                currencyCode = code;
                currencyDigits = CurrencyDisplay.Digits(code);
            }
        }

        if (spec.Notation != NumberNotation.Standard)
        {
            var compact = CompactFormatter.TryFormat(
                number, spec.Notation, spec.Unit, culture, currencySymbol, currencyCode, spec.Grouping);
            if (compact is not null)
            {
                return compact;
            }
            // No compact data (unreachable in practice — root always has data): fall through to the standard path.
        }

        NumberPattern pattern = StandardPatterns.For(spec.Unit, culture);
        int minimum;
        int maximum;
        if (spec.Unit == NumberUnit.Currency)
        {
            (minimum, maximum) = ResolveCurrencyFraction(spec, currencyDigits);
        }
        else
        {
            minimum = spec.MinFractionDigits ?? pattern.MinFractionDigits;
            maximum = spec.MaxFractionDigits ?? pattern.MaxFractionDigits;
            if (maximum < minimum)
            {
                maximum = minimum;
            }
        }

        // Currency threads the CLDR currencySpacing insert (NBSP joiner) so an alphabetic code/symbol
        // sitting directly against the digits gets separated; non-currency passes empty.
        var spacingInsert = spec.Unit == NumberUnit.Currency ? CurrencyDisplay.Spacing(culture) : string.Empty;
        return PatternRenderer.Render(
            pattern, number, PatternPrecision.Fraction(minimum, maximum), spec.Grouping, culture, currencySymbol, currencyCode, spacingInsert);
    }

    // ICU's currency-digits override resolved to a (min, max) fraction pair: an explicit skeleton fraction
    // wins, otherwise the currency's CLDR minor-unit count, with max never below min. Shared by the
    // full-name and glyph/pattern currency paths so both resolve fractions identically.
    private static (int Min, int Max) ResolveCurrencyFraction(NumberFormatSpec spec, int currencyDigits)
    {
        var minimum = spec.MinFractionDigits ?? currencyDigits;
        var maximum = spec.MaxFractionDigits ?? currencyDigits;
        if (maximum < minimum)
        {
            maximum = minimum;
        }

        return (minimum, maximum);
    }

    // Which currency a bare `currency` style / null code uses: the culture's region currency (host selects
    // WHICH currency; CLDR renders HOW). Null when the culture has no region (neutral culture).
    private static string? CultureCurrencyCode(CultureInfo culture)
    {
        try
        {
            return new RegionInfo(culture.Name).ISOCurrencySymbol;
        }
        catch (ArgumentException)
        {
            return null;
        }
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

                try
                {
                    number = (decimal)db;
                    return true;
                }
                catch (OverflowException)
                {
                    // Finite but beyond decimal's range: report false rather than crash, matching the default case.
                    number = 0m;
                    return false;
                }
            case float fl:
                if (float.IsNaN(fl) || float.IsInfinity(fl))
                {
                    number = 0m;
                    return false;
                }

                try
                {
                    number = (decimal)fl;
                    return true;
                }
                catch (OverflowException)
                {
                    number = 0m;
                    return false;
                }
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
