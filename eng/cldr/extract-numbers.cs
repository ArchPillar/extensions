// CLDR number patterns -> eng/cldr/numbers.json + eng/cldr/compact.json (the committed pins).
//
// Downloads the cldr-json `cldr-numbers-full` npm tarball for the given version (the npm registry is
// just where Unicode publishes cldr-json — nothing npm is installed), consolidates the three standard
// patterns (decimal/percent/currency; latn numbering system) into numbers.json and the compact
// notations (short/long decimal, short currency, and its alphaNextToNumber variant) into compact.json,
// one entry per locale, written next to this file. `cldr-numbers-modern` would be the smaller source,
// but it stopped publishing after CLDR 45 (its own npm metadata marks it deprecated); `-full` is its
// maintained, same-shape superset.
// .NET 10 file-based app — run from anywhere:
//     dotnet run eng/cldr/extract-numbers.cs -- 48.2.0
using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

var version = args.Length > 0 ? args[0] : "48.2.0";
var url = $"https://registry.npmjs.org/cldr-numbers-full/-/cldr-numbers-full-{version}.tgz";
Console.WriteLine($"fetching {url}");

using var client = new HttpClient();
await using Stream download = await client.GetStreamAsync(url);
await using var gzip = new GZipStream(download, CompressionMode.Decompress);
await using var tar = new TarReader(gzip);

var locales = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
var compactLocales = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
string? cldrVersion = null;
while (await tar.GetNextEntryAsync() is { } entry)
{
    if (entry.EntryType != TarEntryType.RegularFile || entry.DataStream is null)
    {
        continue;
    }

    if (entry.Name == "package/package.json")
    {
        // The 48.x cldr-json schema carries no per-locale identity.version block; the CLDR
        // version lives in the package manifest. A per-locale identity.version (older schema)
        // still wins if one is present.
        JsonNode manifest = (await JsonNode.ParseAsync(entry.DataStream))!;
        cldrVersion ??= (string?)manifest["cldrVersion"];
        continue;
    }

    if (!entry.Name.StartsWith("package/main/", StringComparison.Ordinal)
        || !entry.Name.EndsWith("/numbers.json", StringComparison.Ordinal))
    {
        continue;
    }

    JsonNode root = (await JsonNode.ParseAsync(entry.DataStream))!;
    KeyValuePair<string, JsonNode?> locale = root["main"]!.AsObject().GetAt(0);
    JsonNode payload = locale.Value!;
    cldrVersion = (string?)payload["identity"]?["version"]?["_cldrVersion"] ?? cldrVersion;
    JsonNode numbers = payload["numbers"]!;
    var decimalPattern = Standard(numbers, "decimalFormats");
    var percentPattern = Standard(numbers, "percentFormats");
    var currencyPattern = Standard(numbers, "currencyFormats");
    if (decimalPattern is null || percentPattern is null || currencyPattern is null)
    {
        continue;
    }

    locales[locale.Key.Replace('_', '-').ToLowerInvariant()] = new JsonObject
    {
        ["currency"] = currencyPattern,
        ["decimal"] = decimalPattern,
        ["percent"] = percentPattern
    };

    JsonNode decimalFormats = numbers["decimalFormats-numberSystem-latn"]!;
    JsonNode currencyFormats = numbers["currencyFormats-numberSystem-latn"]!;
    JsonObject? shortDecimal = Compact(decimalFormats["short"]?["decimalFormat"]);
    JsonObject? longDecimal = Compact(decimalFormats["long"]?["decimalFormat"]);
    JsonObject? shortCurrency = Compact(currencyFormats["short"]?["standard"]);
    JsonObject? shortCurrencyAlpha = CompactAlpha(currencyFormats["short"]?["standard"]);
    if (shortDecimal is not null || longDecimal is not null || shortCurrency is not null)
    {
        var compactEntry = new JsonObject();
        if (shortDecimal is not null) { compactEntry["shortDecimal"] = shortDecimal; }
        if (longDecimal is not null) { compactEntry["longDecimal"] = longDecimal; }
        if (shortCurrency is not null) { compactEntry["shortCurrency"] = shortCurrency; }
        if (shortCurrencyAlpha is not null) { compactEntry["shortCurrencyAlpha"] = shortCurrencyAlpha; }
        compactLocales[locale.Key.Replace('_', '-').ToLowerInvariant()] = compactEntry;
    }
}

if (!locales.ContainsKey("root"))
{
    Console.WriteLine("warning: source had no root locale; pinning CLDR root constants");

    // The separator in the currency pattern is U+00A0 (no-break space), exactly as in CLDR root
    // data (published under main/und); kept as an escape so it cannot silently degrade to U+0020.
    locales["root"] = new JsonObject
    {
        ["currency"] = "¤\u00A0#,##0.00",
        ["decimal"] = "#,##0.###",
        ["percent"] = "#,##0%"
    };
}

var output = new JsonObject
{
    ["version"] = new JsonObject { ["_cldrVersion"] = cldrVersion },
    ["locales"] = new JsonObject(locales.Select(pair => KeyValuePair.Create(pair.Key, (JsonNode?)pair.Value)))
};

var outPath = Path.Combine(ScriptDirectory(), "numbers.json");
var json = output.ToJsonString(new JsonSerializerOptions
{
    WriteIndented = true,
    NewLine = "\n",
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
});
File.WriteAllText(outPath, json + "\n");
Console.WriteLine($"locales={locales.Count} cldr={cldrVersion} -> {outPath}");

var compactOutput = new JsonObject
{
    ["version"] = new JsonObject { ["_cldrVersion"] = cldrVersion },
    ["locales"] = new JsonObject(compactLocales.Select(pair => KeyValuePair.Create(pair.Key, (JsonNode?)pair.Value)))
};
var compactPath = Path.Combine(ScriptDirectory(), "compact.json");
File.WriteAllText(compactPath, compactOutput.ToJsonString(new JsonSerializerOptions
{
    WriteIndented = true,
    NewLine = "\n",
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
}) + "\n");
Console.WriteLine($"compact locales={compactLocales.Count} -> {compactPath}");

static string? Standard(JsonNode numbers, string block) =>
    (string?)numbers[block + "-numberSystem-latn"]?["standard"];

// Reads a `{ "1000-count-one":"0K", ... }` object into { magnitude -> { count -> pattern } },
// keeping only plain `NNNN-count-X` keys (no `-alt-...` suffix). Returns null when the block is absent.
// Magnitudes are parsed as BigInteger: some locales carry powers of ten beyond Int64 range.
static JsonObject? Compact(JsonNode? block)
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
        var count = key[(marker + "-count-".Length)..];
        if (!byMagnitude.TryGetValue(magnitude, out JsonObject? counts))
        {
            counts = new JsonObject();
            byMagnitude[magnitude] = counts;
        }

        counts[count] = (string)pair.Value!;
    }

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

// Reads the alphaNextToNumber currency variant: keys `NNNN-count-X-alt-alphaNextToNumber`.
static JsonObject? CompactAlpha(JsonNode? block)
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
        var count = trimmed[(marker + "-count-".Length)..];
        if (!byMagnitude.TryGetValue(magnitude, out JsonObject? counts))
        {
            counts = new JsonObject();
            byMagnitude[magnitude] = counts;
        }

        counts[count] = (string)pair.Value!;
    }

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

static string ScriptDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
    Path.GetDirectoryName(path)!;
