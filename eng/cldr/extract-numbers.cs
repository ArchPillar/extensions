// CLDR standard number patterns -> eng/cldr/numbers.json (the committed pin).
//
// Downloads the cldr-json `cldr-numbers-full` npm tarball for the given version (the npm registry is
// just where Unicode publishes cldr-json — nothing npm is installed), consolidates the three standard
// patterns (decimal/percent/currency; latn numbering system) per locale, and writes the pin next to
// this file. `cldr-numbers-modern` would be the smaller source, but it stopped publishing after
// CLDR 45 (its own npm metadata marks it deprecated); `-full` is its maintained, same-shape superset.
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

static string? Standard(JsonNode numbers, string block) =>
    (string?)numbers[block + "-numberSystem-latn"]?["standard"];

static string ScriptDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
    Path.GetDirectoryName(path)!;
