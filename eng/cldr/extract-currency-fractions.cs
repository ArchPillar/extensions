// CLDR currency minor-unit digits -> eng/cldr/currency-fractions.json (a committed pin).
//
// Downloads the cldr-json `cldr-core` npm tarball for the given version (the npm registry is just where
// Unicode publishes cldr-json — nothing npm is installed) and reads supplemental/currencyData.json's
// `fractions` map. Only currencies whose `_digits` differs from the DEFAULT of 2 are emitted; every code
// absent from the pin (e.g. USD) is understood to use 2 minor-unit digits.
// .NET 10 file-based app — run from anywhere:
//     dotnet run eng/cldr/extract-currency-fractions.cs -- 48.2.0
using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

var version = args.Length > 0 ? args[0] : "48.2.0";
var url = $"https://registry.npmjs.org/cldr-core/-/cldr-core-{version}.tgz";
Console.WriteLine($"fetching {url}");
using var client = new HttpClient();
await using Stream download = await client.GetStreamAsync(url);
await using var gzip = new GZipStream(download, CompressionMode.Decompress);
await using var tar = new TarReader(gzip);

var digits = new SortedDictionary<string, int>(StringComparer.Ordinal);
string? cldrVersion = null;
while (await tar.GetNextEntryAsync() is { } entry)
{
    if (entry.Name == "package/package.json" && entry.DataStream is not null)
    {
        cldrVersion ??= (string?)(await JsonNode.ParseAsync(entry.DataStream))!["cldrVersion"];
        continue;
    }

    if (!entry.Name.EndsWith("/currencyData.json", StringComparison.Ordinal) || entry.DataStream is null)
    {
        continue;
    }

    JsonNode root = (await JsonNode.ParseAsync(entry.DataStream))!;
    JsonObject fractions = root["supplemental"]!["currencyData"]!["fractions"]!.AsObject();
    foreach ((var code, JsonNode? node) in fractions)
    {
        if (code == "DEFAULT")
        {
            continue;
        }

        var d = int.Parse((string)node!["_digits"]!, System.Globalization.CultureInfo.InvariantCulture);
        if (d != 2)
        {
            digits[code] = d;
        }
    }
}

var output = new JsonObject
{
    ["version"] = new JsonObject { ["_cldrVersion"] = cldrVersion },
    ["digits"] = new JsonObject(digits.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value)))
};
var outPath = Path.Combine(ScriptDirectory(), "currency-fractions.json");
File.WriteAllText(outPath, output.ToJsonString(new JsonSerializerOptions
{
    WriteIndented = true,
    NewLine = "\n",
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
}) + "\n");
Console.WriteLine($"digits={digits.Count} cldr={cldrVersion} -> {outPath}");

static string ScriptDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
    Path.GetDirectoryName(path)!;
