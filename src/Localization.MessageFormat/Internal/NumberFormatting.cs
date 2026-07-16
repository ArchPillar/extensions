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
        if (value is not IFormattable formattable)
        {
            return value?.ToString() ?? string.Empty;
        }

        return spec.Unit switch
        {
            NumberUnit.Currency => FormatCurrency(formattable, spec, culture),
            NumberUnit.Percent => formattable.ToString(BuildFormat(spec, "%"), culture),
            _ => formattable.ToString(BuildFormat(spec, string.Empty), culture)
        };
    }

    private static string FormatCurrency(IFormattable value, NumberFormatSpec spec, CultureInfo culture)
    {
        if (spec.CurrencyCode is null)
        {
            // Named `currency` — the rendering culture's own currency.
            return value.ToString("C", culture);
        }

        (var symbol, var digits) = CurrencyLookup.Resolve(spec.CurrencyCode);
        var format = (NumberFormatInfo)culture.NumberFormat.Clone();
        format.CurrencySymbol = symbol;
        format.CurrencyDecimalDigits = spec.MinFractionDigits ?? digits;
        if (!spec.Grouping)
        {
            format.CurrencyGroupSizes = [0];
        }

        return value.ToString("C", format);
    }

    // Builds a "#,##0.###"-style custom format for decimal/percent: min '0's then optional '#'s, grouped
    // unless disabled. suffix is "%" for percent (the specifier multiplies by 100 and adds the locale sign).
    private static string BuildFormat(NumberFormatSpec spec, string suffix)
    {
        var min = spec.MinFractionDigits ?? 0;
        var max = spec.MaxFractionDigits ?? 3;
        var integer = spec.Grouping ? "#,##0" : "0";
        if (max == 0)
        {
            return integer + suffix;
        }

        var fraction = "." + new string('0', min) + new string('#', max - min);
        return integer + fraction + suffix;
    }
}
