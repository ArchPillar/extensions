using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace ArchPillar.Extensions.Localization.MessageFormat.Internal;

/// <summary>
/// Lazily inflates and parses the pinned CLDR currency-display resource (<see cref="CldrCurrencyData"/>)
/// into lookup tables: minor-unit digits, per-locale currency entries (delta-stored), and per-locale
/// unit patterns + currency spacing. Parsed once on first use and cached. See the Shared Data-Format
/// Contract in the Spec 5 plan for the wire format.
/// </summary>
internal static class CurrencyData
{
    /// <summary>
    /// A single locale's display data for one currency code, as stored (delta-encoded) in the resource.
    /// </summary>
    /// <param name="Symbol">The currency symbol, or the code itself when the locale has none.</param>
    /// <param name="Narrow">The narrow symbol, or empty when it equals <paramref name="Symbol"/>.</param>
    /// <param name="Names">The plural display names keyed by CLDR count (may be empty).</param>
    internal readonly record struct CurrencyEntry(string Symbol, string Narrow, IReadOnlyDictionary<string, string> Names);

    private const char Us = '\u001F';

    private static readonly Lazy<Model> _model = new(Load);

    public static int Digits(string code) => _model.Value.Fractions.TryGetValue(code, out var d) ? d : 2;

    public static bool TryEntry(string locale, string code, out CurrencyEntry entry)
    {
        if (_model.Value.Currency.TryGetValue(locale, out Dictionary<string, CurrencyEntry>? codes)
            && codes.TryGetValue(code, out entry))
        {
            return true;
        }

        entry = default;
        return false;
    }

    public static string Spacing(string locale) =>
        ResolveMeta(locale, out (int Pattern, int Spacing) meta) ? _model.Value.SpacingPool[meta.Spacing] : "\u00A0";

    public static IReadOnlyDictionary<string, string>? UnitPatterns(string locale) =>
        ResolveMeta(locale, out (int Pattern, int Spacing) meta) ? _model.Value.Patterns[meta.Pattern] : null;

    private static bool ResolveMeta(string locale, out (int Pattern, int Spacing) meta)
    {
        var l = locale;
        while (true)
        {
            if (_model.Value.Meta.TryGetValue(l, out meta))
            {
                return true;
            }

            if (l == "root")
            {
                return false;
            }

            var dash = l.IndexOf('-');
#if NETSTANDARD2_0
            l = dash > 0 ? l.Substring(0, dash) : "root";
#else
            l = dash > 0 ? l[..dash] : "root";
#endif
        }
    }

    private static Model Load()
    {
        Assembly assembly = typeof(CurrencyData).Assembly;
        using Stream resource = assembly.GetManifestResourceStream(CldrCurrencyData.ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource '{CldrCurrencyData.ResourceName}'.");
        using var deflate = new DeflateStream(resource, CompressionMode.Decompress);
        using var reader = new StreamReader(deflate, Encoding.UTF8);

        var digits = new Dictionary<string, int>(StringComparer.Ordinal);
        var spacing = new List<string>();
        var patterns = new List<IReadOnlyDictionary<string, string>>();
        var metaMap = new Dictionary<string, (int Pattern, int Spacing)>(StringComparer.OrdinalIgnoreCase);
        var currency = new Dictionary<string, Dictionary<string, CurrencyEntry>>(StringComparer.OrdinalIgnoreCase);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var f = line.Split('\t');
            switch (f[0])
            {
                case "R":
                    digits[f[1]] = int.Parse(f[2], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "K":
                    spacing.Add(f[2]);
                    break;
                case "P":
                    patterns.Add(ParseMap(f[2]));
                    break;
                case "L":
                    metaMap[f[1]] = (int.Parse(f[2], System.Globalization.CultureInfo.InvariantCulture),
                                     int.Parse(f[3], System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case "C":
                    if (!currency.TryGetValue(f[1], out Dictionary<string, CurrencyEntry>? codes))
                    {
                        codes = new Dictionary<string, CurrencyEntry>(StringComparer.OrdinalIgnoreCase);
                        currency[f[1]] = codes;
                    }

                    codes[f[2]] = new CurrencyEntry(f[3], f[4], ParseMap(f[5]));
                    break;
                default:
                    break; // "V" header and any unknown record
            }
        }

        return new Model(digits, spacing.ToArray(), patterns.ToArray(), metaMap, currency);
    }

    private static IReadOnlyDictionary<string, string> ParseMap(string field)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (field.Length == 0)
        {
            return map;
        }

        foreach (var pair in field.Split(Us))
        {
            var eq = pair.IndexOf('=');
#if NETSTANDARD2_0
            map[pair.Substring(0, eq)] = pair.Substring(eq + 1);
#else
            map[pair[..eq]] = pair[(eq + 1)..];
#endif
        }

        return map;
    }

    private sealed record Model(
        Dictionary<string, int> Fractions,
        string[] SpacingPool,
        IReadOnlyDictionary<string, string>[] Patterns,
        Dictionary<string, (int Pattern, int Spacing)> Meta,
        Dictionary<string, Dictionary<string, CurrencyEntry>> Currency);
}
