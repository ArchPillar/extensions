// ---------------------------------------------------------------------------
// Localization.WasmHostSample
//
// An ASP.NET Core server that hosts the Localization.WasmSample Blazor WebAssembly client, which in turn
// references a localized Razor class library. This is the realistic deployment shape — server hosts client,
// client references libraries — and the one where catalog publishing has to compose across three reference
// levels: the library contributes its catalogs, the client (the "authority") merges them into one bundle per
// culture and emits the apl-catalogs.json manifest, and this host serves the client's published wwwroot.
//
// The point of the sample is what the publish output must look like: the client's merged bundle and manifest
// under wwwroot/Translations, and NOT the libraries' raw per-culture catalogs under wwwroot/_content — those
// are an intermediate the authority already merged, and shipping them would be dead weight the manifest never
// points at. See the README for the exact publish assertions.
// ---------------------------------------------------------------------------
var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();
