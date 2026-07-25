// CLDR number patterns -> eng/cldr/numbers.json + eng/cldr/compact.json + eng/cldr/currencies.json (pins).
//
// Downloads the cldr-json `cldr-numbers-full` npm tarball for the given version (the npm registry is
// just where Unicode publishes cldr-json — nothing npm is installed), consolidates the three standard
// patterns (decimal/percent/currency; latn numbering system) into numbers.json, the compact notations
// (short/long decimal, short currency, and its alphaNextToNumber variant) into compact.json, and the
// currency display data (per-code symbol/narrow/names plus unitPattern + spacing) into currencies.json,
// one entry per locale, written next to this file. `cldr-numbers-modern` would be the smaller source, but
// it stopped publishing after CLDR 45 (its own npm metadata marks it deprecated); `-full` is its
// maintained, same-shape superset. The per-entry work lives in the CldrTooling.Cldr helper (a named
// namespace keeps the top-level orchestrator thin and each step individually maintainable).
// .NET 10 file-based app — run from anywhere:
//     dotnet run eng/cldr/extract-numbers.cs -- 48.2.0
using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using CldrTooling;

var version = args.Length > 0 ? args[0] : "48.2.0";
var url = $"https://registry.npmjs.org/cldr-numbers-full/-/cldr-numbers-full-{version}.tgz";
Console.WriteLine($"fetching {url}");

using var client = new HttpClient();
await using Stream download = await client.GetStreamAsync(url);
await using var gzip = new GZipStream(download, CompressionMode.Decompress);
await using var tar = new TarReader(gzip);

var locales = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
var compactLocales = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
var currencyLocales = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
string? cldrVersion = null;
while (await tar.GetNextEntryAsync() is { } entry)
{
    if (entry.EntryType != TarEntryType.RegularFile || entry.DataStream is null)
    {
        continue;
    }

    if (entry.Name == "package/package.json")
    {
        // The 48.x cldr-json schema carries no per-locale identity.version block; the CLDR version lives
        // in the package manifest. A per-locale identity.version (older schema) still wins if present.
        JsonNode manifest = (await JsonNode.ParseAsync(entry.DataStream))!;
        cldrVersion ??= (string?)manifest["cldrVersion"];
        continue;
    }

    // numbers.json and currencies.json are separate tar entries for the same locale, arriving in an
    // arbitrary order; both helpers get-or-create the shared per-locale currency bucket.
    if (Cldr.IsMain(entry.Name, "/currencies.json"))
    {
        JsonNode root = (await JsonNode.ParseAsync(entry.DataStream))!;
        Cldr.AddCurrencies(currencyLocales, root);
        continue;
    }

    if (Cldr.IsMain(entry.Name, "/numbers.json"))
    {
        JsonNode root = (await JsonNode.ParseAsync(entry.DataStream))!;
        cldrVersion = Cldr.AddNumbers(locales, compactLocales, currencyLocales, root) ?? cldrVersion;
    }
}

Cldr.EnsureRoot(locales);
Cldr.WritePin("numbers.json", cldrVersion, locales);
Cldr.RemapUndToRoot(compactLocales);
Cldr.WritePin("compact.json", cldrVersion, compactLocales);
Cldr.WritePin("currencies.json", cldrVersion, currencyLocales);

namespace CldrTooling
{
    internal static class Cldr
    {
        public static bool IsMain(string name, string suffix) =>
            name.StartsWith("package/main/", StringComparison.Ordinal)
            && name.EndsWith(suffix, StringComparison.Ordinal);

        // currencies.json -> { CODE -> { symbol, narrow?, names? } } merged into the shared bucket.
        public static void AddCurrencies(SortedDictionary<string, JsonObject> currencyLocales, JsonNode root)
        {
            KeyValuePair<string, JsonNode?> loc = root["main"]!.AsObject().GetAt(0);
            var localeKey = loc.Key.Replace('_', '-').ToLowerInvariant();
            JsonObject currencies = loc.Value!["numbers"]!["currencies"]!.AsObject();
            Bucket(currencyLocales, localeKey)["currencies"] = CurrencyCodes(currencies);
        }

        // numbers.json -> standard patterns + compact notations + currency display bits. Returns the
        // per-locale identity CLDR version if the older schema carries one (null on 48.x).
        public static string? AddNumbers(
            SortedDictionary<string, JsonObject> locales,
            SortedDictionary<string, JsonObject> compactLocales,
            SortedDictionary<string, JsonObject> currencyLocales,
            JsonNode root)
        {
            KeyValuePair<string, JsonNode?> locale = root["main"]!.AsObject().GetAt(0);
            JsonNode payload = locale.Value!;
            var identityVersion = (string?)payload["identity"]?["version"]?["_cldrVersion"];
            JsonNode numbers = payload["numbers"]!;
            var decimalPattern = Standard(numbers, "decimalFormats");
            var percentPattern = Standard(numbers, "percentFormats");
            var currencyPattern = Standard(numbers, "currencyFormats");
            if (decimalPattern is null || percentPattern is null || currencyPattern is null)
            {
                return identityVersion;
            }

            var localeKey = locale.Key.Replace('_', '-').ToLowerInvariant();
            locales[localeKey] = new JsonObject
            {
                ["currency"] = currencyPattern,
                ["decimal"] = decimalPattern,
                ["percent"] = percentPattern
            };

            JsonNode currencyFormats = numbers["currencyFormats-numberSystem-latn"]!;
            JsonObject? compact = CompactEntry(numbers["decimalFormats-numberSystem-latn"]!, currencyFormats);
            if (compact is not null)
            {
                compactLocales[localeKey] = compact;
            }

            // Currency display bits share the currencyFormats node: unitPattern-count-* templates and the
            // beforeCurrency spacing (default U+00A0, kept as an escape, never a literal NBSP).
            JsonObject cf = currencyFormats.AsObject();
            JsonObject bucket = Bucket(currencyLocales, localeKey);
            bucket["unitPattern"] = UnitPatterns(cf);
            bucket["spacing"] = (string?)cf["currencySpacing"]?["beforeCurrency"]?["insertBetween"] ?? "\u00A0";
            return identityVersion;
        }

        // CLDR ships no 'root' under main/*; the standard resolver terminates at 'root', so pin the CLDR
        // root constants when the source lacked it (matches the historical behavior of this extractor).
        public static void EnsureRoot(SortedDictionary<string, JsonObject> locales)
        {
            if (locales.ContainsKey("root"))
            {
                return;
            }

            Console.WriteLine("warning: source had no root locale; pinning CLDR root constants");
            locales["root"] = new JsonObject
            {
                ["currency"] = "¤\u00A0#,##0.00",
                ["decimal"] = "#,##0.###",
                ["percent"] = "#,##0%"
            };
        }

        // CLDR publishes root compact data under 'und'; the resolver terminates at 'root'.
        public static void RemapUndToRoot(SortedDictionary<string, JsonObject> compactLocales)
        {
            if (!compactLocales.ContainsKey("root") && compactLocales.TryGetValue("und", out JsonObject? undCompact))
            {
                compactLocales["root"] = undCompact;
                compactLocales.Remove("und");
            }
        }

        // Writes a `{version, locales:{...}}` pin next to this script with the shared serializer options.
        public static void WritePin(string fileName, string? cldrVersion, SortedDictionary<string, JsonObject> map)
        {
            var output = new JsonObject
            {
                ["version"] = new JsonObject { ["_cldrVersion"] = cldrVersion },
                ["locales"] = new JsonObject(map.Select(pair => KeyValuePair.Create(pair.Key, (JsonNode?)pair.Value)))
            };
            var path = Path.Combine(ScriptDirectory(), fileName);
            File.WriteAllText(path, output.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                NewLine = "\n",
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }) + "\n");
            Console.WriteLine($"{fileName}: {map.Count} locales, cldr={cldrVersion} -> {path}");
        }

        private static JsonObject CurrencyCodes(JsonObject currencies)
        {
            var codes = new JsonObject();
            foreach ((var code, JsonNode? node) in currencies)
            {
                JsonObject o = node!.AsObject();
                var names = new JsonObject();
                foreach ((var k, JsonNode? v) in o)
                {
                    if (k.StartsWith("displayName-count-", StringComparison.Ordinal))
                    {
                        names[k["displayName-count-".Length..]] = (string)v!;
                    }
                }

                if (!names.ContainsKey("other") && (string?)o["displayName"] is { } bare)
                {
                    names["other"] = bare;
                }

                var entryObj = new JsonObject { ["symbol"] = (string?)o["symbol"] ?? code };
                if ((string?)o["symbol-alt-narrow"] is { } narrow)
                {
                    entryObj["narrow"] = narrow;
                }

                if (names.Count > 0)
                {
                    entryObj["names"] = names;
                }

                codes[code] = entryObj;
            }

            return codes;
        }

        private static JsonObject UnitPatterns(JsonObject cf)
        {
            var unitPatterns = new JsonObject();
            foreach ((var key, JsonNode? node) in cf)
            {
                if (key.StartsWith("unitPattern-count-", StringComparison.Ordinal))
                {
                    unitPatterns[key["unitPattern-count-".Length..]] = (string)node!;
                }
            }

            return unitPatterns;
        }

        private static JsonObject Bucket(SortedDictionary<string, JsonObject> map, string key)
        {
            if (!map.TryGetValue(key, out JsonObject? bucket))
            {
                bucket = new JsonObject();
                map[key] = bucket;
            }

            return bucket;
        }

        private static JsonObject? CompactEntry(JsonNode decimalFormats, JsonNode currencyFormats)
        {
            JsonObject? shortDecimal = Compact(decimalFormats["short"]?["decimalFormat"]);
            JsonObject? longDecimal = Compact(decimalFormats["long"]?["decimalFormat"]);
            JsonObject? shortCurrency = Compact(currencyFormats["short"]?["standard"]);
            JsonObject? shortCurrencyAlpha = CompactAlpha(currencyFormats["short"]?["standard"]);
            if (shortDecimal is null && longDecimal is null && shortCurrency is null)
            {
                return null;
            }

            var compactEntry = new JsonObject();
            if (shortDecimal is not null) { compactEntry["shortDecimal"] = shortDecimal; }
            if (longDecimal is not null) { compactEntry["longDecimal"] = longDecimal; }
            if (shortCurrency is not null) { compactEntry["shortCurrency"] = shortCurrency; }
            if (shortCurrencyAlpha is not null) { compactEntry["shortCurrencyAlpha"] = shortCurrencyAlpha; }
            return compactEntry;
        }

        private static string? Standard(JsonNode numbers, string block) =>
            (string?)numbers[block + "-numberSystem-latn"]?["standard"];

        // { "1000-count-one":"0K", ... } -> { magnitude -> { count -> pattern } }, plain NNNN-count-X keys
        // only (no -alt- suffix). Magnitudes are BigInteger: some locales exceed Int64 range.
        private static JsonObject? Compact(JsonNode? block)
        {
            if (block is not JsonObject entries)
            {
                return null;
            }

            var byMagnitude = new SortedDictionary<System.Numerics.BigInteger, JsonObject>();
            foreach (KeyValuePair<string, JsonNode?> pair in entries)
            {
                var key = pair.Key;
                var marker = key.IndexOf("-count-", StringComparison.Ordinal);
                if (marker < 0 || key.Contains("-alt-", StringComparison.Ordinal))
                {
                    continue;
                }

                var magnitude = System.Numerics.BigInteger.Parse(key[..marker], System.Globalization.CultureInfo.InvariantCulture);
                Add(byMagnitude, magnitude, key[(marker + "-count-".Length)..], (string)pair.Value!);
            }

            return Materialize(byMagnitude);
        }

        // The alphaNextToNumber currency variant: keys NNNN-count-X-alt-alphaNextToNumber.
        private static JsonObject? CompactAlpha(JsonNode? block)
        {
            if (block is not JsonObject entries)
            {
                return null;
            }

            var byMagnitude = new SortedDictionary<System.Numerics.BigInteger, JsonObject>();
            foreach (KeyValuePair<string, JsonNode?> pair in entries)
            {
                const string Alt = "-alt-alphaNextToNumber";
                var key = pair.Key;
                if (!key.EndsWith(Alt, StringComparison.Ordinal))
                {
                    continue;
                }

                var trimmed = key[..^Alt.Length];
                var marker = trimmed.IndexOf("-count-", StringComparison.Ordinal);
                if (marker < 0)
                {
                    continue;
                }

                var magnitude = System.Numerics.BigInteger.Parse(trimmed[..marker], System.Globalization.CultureInfo.InvariantCulture);
                Add(byMagnitude, magnitude, trimmed[(marker + "-count-".Length)..], (string)pair.Value!);
            }

            return Materialize(byMagnitude);
        }

        private static void Add(
            SortedDictionary<System.Numerics.BigInteger, JsonObject> byMagnitude,
            System.Numerics.BigInteger magnitude,
            string count,
            string pattern)
        {
            if (!byMagnitude.TryGetValue(magnitude, out JsonObject? counts))
            {
                counts = new JsonObject();
                byMagnitude[magnitude] = counts;
            }

            counts[count] = pattern;
        }

        private static JsonObject? Materialize(SortedDictionary<System.Numerics.BigInteger, JsonObject> byMagnitude)
        {
            if (byMagnitude.Count == 0)
            {
                return null;
            }

            var result = new JsonObject();
            foreach (KeyValuePair<System.Numerics.BigInteger, JsonObject> pair in byMagnitude)
            {
                result[pair.Key.ToString(System.Globalization.CultureInfo.InvariantCulture)] = pair.Value;
            }

            return result;
        }

        private static string ScriptDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
            Path.GetDirectoryName(path)!;
    }
}
