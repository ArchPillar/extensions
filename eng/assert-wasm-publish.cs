// Asserts the published output of the hosted Blazor WebAssembly sample (Localization.WasmHostSample) is correct,
// for the CI wasm-publish job. Kept as a .NET file-based app (like the rest of eng/) rather than inline CI shell:
// it parses the static-web-asset and catalog JSON natively, runs the published host and checks it over HTTP, and
// can be run the same way locally —
//
//     dotnet run eng/assert-wasm-publish.cs -- <publish-dir> <host-obj-dir>
//
// e.g. dotnet run eng/assert-wasm-publish.cs -- artifacts/wasmhost samples/Localization/Localization.WasmHostSample/obj
//
// It guards the hosted-publish path that a standalone WASM publish cannot reach (see the package targets): the
// authority's merged bundle and apl-catalogs.json manifest must reach the *host's* wwwroot and be AssetMode=All
// (a CurrentProject asset does not cross the ProjectReference on SDK 9), the library's raw catalogs must not leak,
// and every file the served manifest lists must actually resolve (a manifest that loads but whose entries 404 is
// its own failure mode).
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: dotnet run eng/assert-wasm-publish.cs -- <publish-dir> <host-obj-dir>");
    return 2;
}

var publishDir = Path.GetFullPath(args[0]);
var hostObjDir = Path.GetFullPath(args[1]);
var wwwroot = Path.Combine(publishDir, "wwwroot");
var translations = Path.Combine(wwwroot, "Translations");

var failed = false;

void Broken(string message)
{
    Console.WriteLine($"BROKEN: {message}");
    failed = true;
}

// 1. The manifest the WebAssembly client fetches to discover its catalogs.
var manifestPath = Path.Combine(translations, "apl-catalogs.json");
if (!File.Exists(manifestPath))
{
    Broken($"the catalog manifest is missing at {Rel(manifestPath)}");
}

// 2. At least one merged per-culture bundle.
var bundles = Directory.Exists(translations)
    ? Directory.GetFiles(translations, "*.arb")
    : [];
if (bundles.Length == 0)
{
    Broken("no merged per-culture bundle (*.arb) under wwwroot/Translations");
}

// 3. The merged bundle must carry the referenced library's strings, not only the app's own — a manifest listing a
//    bundle that lost every contributed translation would still pass a bare existence check. The library ships a
//    "greeting" key, so its presence in the merged bundle proves the contributor's catalog was folded in.
foreach (var bundle in bundles)
{
    using var doc = JsonDocument.Parse(File.ReadAllText(bundle));
    var hasContributorKey = doc.RootElement.EnumerateObject().Any(p => p.Name.Contains("greeting", StringComparison.Ordinal));
    if (!hasContributorKey)
    {
        Broken($"the merged bundle {Rel(bundle)} does not contain the contributor library's strings");
    }
}

// 4. The library's raw per-culture catalogs must not leak under _content: the authority already merged them, and
//    a WebAssembly client cannot enumerate a directory, so the copies would be unreachable dead weight.
var content = Path.Combine(wwwroot, "_content");
var leaked = Directory.Exists(content)
    ? Directory.EnumerateFiles(content, "*", SearchOption.AllDirectories)
        .Where(f => f.Replace('\\', '/').Contains("/Translations/") && IsCatalog(f))
        .ToArray()
    : [];
foreach (var f in leaked)
{
    Broken($"raw library catalog leaked into the host wwwroot instead of being merged: {Rel(f)}");
}

// 5. No beside-the-binary file copy at the host output root: a WebAssembly app serves catalogs as static web
//    assets and never reads the file system, so a $(PublishDir)Translations copy is unreachable dead weight.
var rootTranslations = Path.Combine(publishDir, "Translations");
var rootCopies = Directory.Exists(rootTranslations)
    ? Directory.GetFiles(rootTranslations).Where(IsCatalog).ToArray()
    : [];
foreach (var f in rootCopies)
{
    Broken($"catalog file-system copy leaked to the host output root (useless for a WASM client): {Rel(f)}");
}

// 6. The whole bug this sample guards: the authority's bundle + manifest must be AssetMode=All so they cross the
//    ProjectReference into the host. On SDK 9 a CurrentProject asset does not, and the host ships without them.
//    Assert on the host's own resolved publish manifest — SDK-independent, unlike the file tree (SDK 10 forwards
//    CurrentProject and would mask the gap).
var swaManifest = Directory.Exists(hostObjDir)
    ? Directory.GetFiles(hostObjDir, "staticwebassets.publish.json", SearchOption.AllDirectories).FirstOrDefault()
    : null;
if (swaManifest is null)
{
    Broken($"no staticwebassets.publish.json under {Rel(hostObjDir)}");
}
else
{
    using var doc = JsonDocument.Parse(File.ReadAllText(swaManifest));
    string? mode = null;
    if (doc.RootElement.TryGetProperty("Assets", out JsonElement assets))
    {
        foreach (JsonElement asset in assets.EnumerateArray())
        {
            var relativePath = asset.TryGetProperty("RelativePath", out JsonElement r) ? r.GetString() ?? "" : "";
            if (relativePath.Contains("apl-catalogs", StringComparison.Ordinal))
            {
                mode = asset.TryGetProperty("AssetMode", out JsonElement m) ? m.GetString() : null;
                break;
            }
        }
    }

    Console.WriteLine($"host apl-catalogs asset AssetMode: {mode ?? "MISSING"}");
    if (mode != "All")
    {
        Broken($"the authority manifest is '{mode ?? "MISSING"}' in the host publish set, not All — it will not reach a hosted wwwroot");
    }
}

// 7. End-to-end: run the published host and assert the manifest and every file it lists return 200. Fingerprinting
//    rewrites served paths while the manifest is written from bare file names, and .arb is an unknown content type
//    to plain UseStaticFiles — so a manifest that loads but whose entries 404 is a distinct failure mode.
if (File.Exists(manifestPath))
{
    await AssertServedAsync(publishDir);
}

Console.WriteLine(failed
    ? "FAILED: see BROKEN lines above"
    : "OK: manifest + merged bundle (with library strings) present, no leaks, authority AssetMode=All, every listed catalog serves 200");
return failed ? 1 : 0;

async Task AssertServedAsync(string appDir)
{
    // The publish output holds two runtimeconfigs — the server host and the WebAssembly client copied into it.
    // Run the host: it is the framework-dependent one (references Microsoft.AspNetCore.App), whereas the client's
    // is self-contained (no framework). Run it from the publish directory so its content root (and wwwroot)
    // resolve as a real deployment does.
    var runtimeConfig = Directory.GetFiles(appDir, "*.runtimeconfig.json").FirstOrDefault(IsFrameworkDependent);
    if (runtimeConfig is null)
    {
        Broken($"no framework-dependent host assembly (*.runtimeconfig.json) in {Rel(appDir)}");
        return;
    }

    var dll = Path.GetFileName(runtimeConfig)[..^".runtimeconfig.json".Length] + ".dll";
    var baseUri = $"http://{IPAddress.Loopback}:5080";

    using var process = new Process();
    process.StartInfo = new ProcessStartInfo("dotnet", $"{dll} --urls {baseUri}")
    {
        WorkingDirectory = appDir,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };

    // Drain the host's stdout/stderr so a chatty startup cannot fill the pipe buffer and block the process before
    // it starts listening; keep the tail to print if it never comes up.
    var hostLog = new StringBuilder();
    void Capture(object _, DataReceivedEventArgs e)
    {
        if (e.Data is not null)
        {
            lock (hostLog)
            {
                hostLog.AppendLine(e.Data);
            }
        }
    }

    process.OutputDataReceived += Capture;
    process.ErrorDataReceived += Capture;
    process.Start();
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();

    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        // Wait for the host to come up (poll the root, not the manifest, so the readiness probe never masks a
        // manifest failure).
        var ready = false;
        for (var i = 0; i < 30 && !ready; i++)
        {
            try
            {
                using HttpResponseMessage probe = await http.GetAsync($"{baseUri}/");
                ready = probe.StatusCode == HttpStatusCode.OK;
            }
            catch (HttpRequestException)
            {
                // not up yet
            }

            if (!ready)
            {
                await Task.Delay(1000);
            }
        }

        if (!ready)
        {
            Broken("the published host did not start serving");
            lock (hostLog)
            {
                Console.WriteLine(hostLog.ToString());
            }

            return;
        }

        using HttpResponseMessage manifestResponse = await http.GetAsync($"{baseUri}/Translations/apl-catalogs.json");
        var manifestBody = await manifestResponse.Content.ReadAsStringAsync();
        Console.WriteLine($"GET /Translations/apl-catalogs.json -> {(int)manifestResponse.StatusCode} ({manifestBody.Length} bytes)");
        if (manifestResponse.StatusCode != HttpStatusCode.OK || manifestBody.Length == 0)
        {
            Broken("the host did not serve the manifest as a non-empty 200");
            return;
        }

        using var doc = JsonDocument.Parse(manifestBody);
        var files = doc.RootElement.GetProperty("catalogs").EnumerateArray()
            .Select(c => c.GetProperty("file").GetString())
            .Where(f => !string.IsNullOrEmpty(f))
            .ToArray();
        if (files.Length == 0)
        {
            Broken("the served manifest lists no catalogs");
            return;
        }

        foreach (var file in files)
        {
            using HttpResponseMessage response = await http.GetAsync($"{baseUri}/Translations/{file}");
            Console.WriteLine($"GET /Translations/{file} -> {(int)response.StatusCode}");
            if (response.StatusCode != HttpStatusCode.OK)
            {
                Broken($"the manifest lists {file} but it did not serve 200");
            }
        }
    }
    finally
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
    }
}

static bool IsCatalog(string path)
{
    var ext = Path.GetExtension(path);
    return ext is ".arb" or ".xliff" or ".xlf" or ".po";
}

static bool IsFrameworkDependent(string runtimeConfigPath)
{
    using var doc = JsonDocument.Parse(File.ReadAllText(runtimeConfigPath));
    return doc.RootElement.TryGetProperty("runtimeOptions", out JsonElement options)
        && (options.TryGetProperty("framework", out _) || options.TryGetProperty("frameworks", out _));
}

string Rel(string path) => Path.GetRelativePath(Directory.GetCurrentDirectory(), path);
