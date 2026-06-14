using ArchPillar.Extensions.Localization;
using Localization.WasmSample;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Ambient = ArchPillar.Extensions.Localization.Localizer;

// ---------------------------------------------------------------------------
// Localization.WasmSample
//
// Demonstrates ArchPillar.Extensions.Localization in a Blazor WebAssembly client:
//   - Discovering and fetching catalogs over HTTP from static web assets (no file system in the browser)
//   - Injecting ILocalizer and IStringLocalizer<T> into components over the ambient store
//   - Switching culture in code at runtime with a button — no reload, no request middleware
//   - A missing override surfacing via IStringLocalizer's ResourceNotFound (the key shows through)
//
// The UI lives in Pages/Home.razor; the German catalog is wwwroot/Translations/Localization.WasmSample.de.arb.
// ---------------------------------------------------------------------------
WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Blazor WebAssembly has no readable file system, so the directory source finds nothing. Instead the catalogs
// are fetched over HTTP from the app's static web assets and layered into the ambient store. The build emits a
// manifest (wwwroot/Translations/apl-catalogs.json) listing the catalogs, so the loader discovers what to fetch
// with no hand-kept list. English ships in code, so only the German override is fetched. AddArchPillarString-
// Localizer then registers the native views and the IStringLocalizer adapter over that same store.
using var http = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
await Ambient.AddCatalogsFromManifestAsync(http);

builder.Services.AddArchPillarStringLocalizer(new LocalizerOptions { SourceCulture = "en" });

await builder.Build().RunAsync();
