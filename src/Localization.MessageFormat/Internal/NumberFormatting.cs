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

        var currencySymbol = string.Empty;
        var currencyCode = string.Empty;
        var currencyDigits = 0;
        if (spec.Unit == NumberUnit.Currency)
        {
            (currencySymbol, currencyCode, currencyDigits) = ResolveCurrency(spec.CurrencyCode, culture);
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
            // ICU's currency-digits override: minor units win over the pattern's fraction body.
            minimum = spec.MinFractionDigits ?? currencyDigits;
            maximum = spec.MaxFractionDigits ?? currencyDigits;
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

    // Resolves the display symbol, ISO code, and default minor-unit digits for a currency spec. A null code
    // means the rendering culture's own currency; an explicit code goes through the CLDR CurrencyDisplay
    // resolver. TEMPORARY: this always uses the Short width; width-aware routing (narrow/iso-code/full-name)
    // lands in Task 7. For now it keeps Short-currency working against CLDR data.
    private static (string Symbol, string Code, int Digits) ResolveCurrency(string? currencyCode, CultureInfo culture)
    {
        if (currencyCode is null)
        {
            NumberFormatInfo info = culture.NumberFormat;
            return (info.CurrencySymbol, string.Empty, info.CurrencyDecimalDigits);
        }

        var symbol = CurrencyDisplay.Glyph(currencyCode, culture, CurrencyWidth.Short);
        return (symbol, currencyCode, CurrencyDisplay.Digits(currencyCode));
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
