// Downloads the pinned ICU4C Win64 runtime (DLLs + icuinfo) into eng/oracle/icu/ — the dev-only
// oracle backing icu-format.cs. Never shipped with the library; eng/oracle/icu/ is gitignored.
//
// .NET 10 file-based app:
//     dotnet run eng/oracle/fetch-icu.cs
using System.IO.Compression;

const string IcuVersion = "78.3";
var url = $"https://github.com/unicode-org/icu/releases/download/release-{IcuVersion}/icu4c-{IcuVersion}-Win64-MSVC2022.zip";
var here = ScriptDirectory();
var target = Path.Combine(here, "icu");
Directory.CreateDirectory(target);

Console.WriteLine($"fetching {url}");
using var client = new HttpClient();
client.DefaultRequestHeaders.Add("User-Agent", "archpillar-oracle-fetch");
var zipBytes = await client.GetByteArrayAsync(url);
Console.WriteLine($"downloaded {zipBytes.Length / (1024 * 1024)} MiB");

using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
var extracted = 0;
foreach (ZipArchiveEntry entry in zip.Entries)
{
    // Runtime lives under a bin64/ folder. We need the three libraries (data, common, i18n) and icuinfo.exe.
    if (!entry.FullName.Replace('\\', '/').Contains("bin64/", StringComparison.OrdinalIgnoreCase))
    {
        continue;
    }

    var name = entry.Name;
    var keep = name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
        || name.Equals("icuinfo.exe", StringComparison.OrdinalIgnoreCase);
    if (!keep)
    {
        continue;
    }

    var outPath = Path.Combine(target, name);
    entry.ExtractToFile(outPath, overwrite: true);
    extracted++;
}

Console.WriteLine($"extracted {extracted} files -> {target}");
foreach (var file in Directory.GetFiles(target).OrderBy(f => f))
{
    Console.WriteLine($"   {Path.GetFileName(file)}  ({new FileInfo(file).Length / 1024} KiB)");
}

static string ScriptDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
    Path.GetDirectoryName(path)!;
