// ---------------------------------------------------------------------------
// Localization.WasmHostSample
//
// An ASP.NET Core server that hosts the Localization.WasmSample Blazor WebAssembly client, which references a
// localized Razor class library. This is the realistic modern deployment — server references client via the
// static web asset pipeline (no Microsoft.AspNetCore.Components.WebAssembly.Server wholesale copy) — and the one
// where catalog publishing has to compose across three reference levels AND where AssetMode decides whether the
// client's merged bundle and apl-catalogs.json manifest reach the deployed wwwroot at all.
//
// MapStaticAssets (not plain UseStaticFiles) serves the composed assets: it honours the catalog content-type
// mappings the package targets register (.arb is application/json, etc.) and the fingerprinted routes, so a
// GET for a bare file name the manifest lists (e.g. /Translations/de.arb) resolves. Plain UseStaticFiles would
// 404 on the unknown .arb type.
// ---------------------------------------------------------------------------
var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapStaticAssets();
app.MapFallbackToFile("index.html");

app.Run();
