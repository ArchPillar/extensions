namespace ArchPillar.Extensions.Localization.MessageFormat.Internal;

/// <summary>
/// Parses the supported subset of ICU number skeletons — the <c>::</c>-prefixed style of a
/// <c>{arg, number, ::…}</c> placeholder — into a <see cref="NumberFormatSpec"/>. Supported stems:
/// <c>currency/&lt;ISO&gt;</c>, fraction precision (<c>.00</c>/<c>.##</c>/<c>.0#</c>),
/// <c>precision-integer</c> (or <c>.</c>), <c>percent</c> (or <c>%</c>), <c>group-off</c>/<c>group-auto</c>,
/// and compact notation (<c>compact-short</c>/<c>K</c>, <c>compact-long</c>/<c>KK</c>).
/// Any other stem throws.
/// </summary>
internal static class NumberSkeleton
{
    private const string CurrencyPrefix = "currency/";

    public static NumberFormatSpec Parse(string skeleton)
    {
        // The input includes the leading "::"; the stems that follow are whitespace-separated.
#if NETSTANDARD2_0
        var body = skeleton.Substring(2);
#else
        var body = skeleton[2..];
#endif
        var stems = body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        NumberUnit unit = NumberUnit.Decimal;
        string? currencyCode = null;
        int? min = null;
        int? max = null;
        var grouping = true;
        var integer = false;
        NumberNotation notation = NumberNotation.Standard;
        CurrencyWidth width = CurrencyWidth.Short;

        foreach (var stem in stems)
        {
            if (stem.StartsWith(CurrencyPrefix, StringComparison.Ordinal))
            {
                unit = NumberUnit.Currency;
#if NETSTANDARD2_0
                currencyCode = stem.Substring(CurrencyPrefix.Length);
#else
                currencyCode = stem[CurrencyPrefix.Length..];
#endif
                if (!IsThreeAsciiLetters(currencyCode))
                {
                    throw Unsupported($"malformed currency skeleton '{stem}' (expected a three-letter ISO code)");
                }
            }
            else if (stem is "percent" or "%")
            {
                unit = NumberUnit.Percent;
            }
            else if (stem is "precision-integer" or ".")
            {
                integer = true;
            }
            else if (stem is "group-off" or ",_")
            {
                grouping = false;
            }
            else if (stem == "group-auto")
            {
                grouping = true;
            }
            else if (stem is "compact-short" or "K")
            {
                notation = NumberNotation.CompactShort;
            }
            else if (stem is "compact-long" or "KK")
            {
                notation = NumberNotation.CompactLong;
            }
            else if (stem is "unit-width-short")
            {
                width = CurrencyWidth.Short;
            }
            else if (stem is "unit-width-narrow")
            {
                width = CurrencyWidth.Narrow;
            }
            else if (stem is "unit-width-iso-code")
            {
                width = CurrencyWidth.IsoCode;
            }
            else if (stem is "unit-width-full-name")
            {
                width = CurrencyWidth.FullName;
            }
            else if (stem[0] == '.')
            {
                (min, max) = ParseFraction(stem);
            }
            else
            {
                throw Unsupported($"unsupported skeleton stem '{stem}'");
            }
        }

        if (integer)
        {
            min = 0;
            max = 0;
        }

        if (notation != NumberNotation.Standard)
        {
            if (min is not null || max is not null)
            {
                throw Unsupported("compact notation cannot combine with a fraction or precision override");
            }

            if (unit == NumberUnit.Percent)
            {
                throw Unsupported("compact notation is not supported for percent");
            }
        }

        if (width != CurrencyWidth.Short)
        {
            if (unit != NumberUnit.Currency)
            {
                throw Unsupported("unit width applies only to currency");
            }

            if (notation != NumberNotation.Standard)
            {
                throw Unsupported("a currency width other than short is not supported with compact notation");
            }
        }

        return new NumberFormatSpec(unit, currencyCode, min, max, grouping, notation, width);
    }

    // A fraction stem: '.' then leading '0's (minimum digits) then trailing '#'s (additional maximum digits).
    private static (int Min, int Max) ParseFraction(string stem)
    {
#if NETSTANDARD2_0
        var digits = stem.Substring(1);
#else
        var digits = stem[1..];
#endif
        var min = 0;
        var max = 0;
        var seenOptional = false;
        foreach (var c in digits)
        {
            if (c == '0')
            {
                if (seenOptional)
                {
                    throw Unsupported($"malformed fraction skeleton '{stem}'");
                }

                min++;
                max++;
            }
            else if (c == '#')
            {
                seenOptional = true;
                max++;
            }
            else
            {
                throw Unsupported($"malformed fraction skeleton '{stem}'");
            }
        }

        return (min, max);
    }

    // ICU requires a currency code of exactly three ASCII letters, case-insensitive (USD/usd valid; 123/u$d/US$
    // rejected). netstandard2.0 has no char.IsAsciiLetter, so the A–Z/a–z range check is inlined.
    private static bool IsThreeAsciiLetters(string code)
    {
        if (code.Length != 3)
        {
            return false;
        }

        foreach (var c in code)
        {
            if (c is not (>= 'A' and <= 'Z' or >= 'a' and <= 'z'))
            {
                return false;
            }
        }

        return true;
    }

    private static MessageFormatException Unsupported(string message) => new(message, -1);
}
