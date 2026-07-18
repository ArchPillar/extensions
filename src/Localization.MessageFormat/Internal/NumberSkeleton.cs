namespace ArchPillar.Extensions.Localization.MessageFormat.Internal;

/// <summary>
/// Parses the supported subset of ICU number skeletons — the <c>::</c>-prefixed style of a
/// <c>{arg, number, ::…}</c> placeholder — into a <see cref="NumberFormatSpec"/>. Supported stems:
/// <c>currency/&lt;ISO&gt;</c>, fraction precision (<c>.00</c>/<c>.##</c>/<c>.0#</c>),
/// <c>precision-integer</c> (or <c>.</c>), <c>percent</c> (or <c>%</c>), and <c>group-off</c>/<c>group-auto</c>.
/// Any other stem — including compact notation, reserved for a later spec — throws.
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
                if (currencyCode.Length != 3)
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

        return new NumberFormatSpec(unit, currencyCode, min, max, grouping);
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

    private static MessageFormatException Unsupported(string message) => new(message, -1);
}
