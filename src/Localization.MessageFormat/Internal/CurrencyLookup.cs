using System.Collections.Concurrent;
using System.Globalization;

namespace ArchPillar.Extensions.Localization.MessageFormat.Internal;

/// <summary>
/// Resolves an ISO 4217 currency code to a display symbol and its CLDR minor-unit count. .NET has no
/// direct ISO-code lookup, so this scans specific cultures for the first <see cref="RegionInfo"/> whose
/// <see cref="RegionInfo.ISOCurrencySymbol"/> matches, taking that region's symbol and that culture's
/// <see cref="NumberFormatInfo.CurrencyDecimalDigits"/> (CLDR-sourced on modern .NET). Results are cached
/// per code; an unmatched code falls back to the code itself and two digits.
/// </summary>
internal static class CurrencyLookup
{
    private static readonly ConcurrentDictionary<string, (string Symbol, int Digits)> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static (string Symbol, int Digits) Resolve(string code) => _cache.GetOrAdd(code, Lookup);

    private static (string Symbol, int Digits) Lookup(string code)
    {
        foreach (CultureInfo culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            RegionInfo region;
            try
            {
                region = new RegionInfo(culture.Name);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (string.Equals(region.ISOCurrencySymbol, code, StringComparison.OrdinalIgnoreCase))
            {
                return (region.CurrencySymbol, culture.NumberFormat.CurrencyDecimalDigits);
            }
        }

        return (code.ToUpperInvariant(), 2);
    }
}
