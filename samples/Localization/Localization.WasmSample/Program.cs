using ArchPillar.Extensions.Localization;
using Localization.WasmSample;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;

// ---------------------------------------------------------------------------
// Localization.WasmSample
//
// Demonstrates ArchPillar.Extensions.Localization in a Blazor WebAssembly client:
//   - Fetching catalogs over HTTP from static web assets (no file system in the browser)
//   - Loading only the active language at startup, and the rest on demand when the app selects one
//   - Injecting ILocalizer and IStringLocalizer<T> into components over the ambient store
//   - Selecting a language at runtime with a button — no reload, no request middleware
//   - A missing override surfacing via IStringLocalizer's ResourceNotFound (the key shows through)
//
// The UI lives in Pages/Home.razor; the German catalog is wwwroot/Translations/Localization.WasmSample.de.xliff.
// ---------------------------------------------------------------------------
WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The Blazor template's app-lifetime HttpClient over the host base address; the catalog loader reuses it.
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddArchPillarStringLocalizer(new LocalizerOptions { SourceCulture = "en" });

WebAssemblyHost host = builder.Build();

// No file system in the browser, so catalogs are fetched over HTTP from the app's static web assets (a PWA caches
// them up front). This registers the build-emitted manifest (wwwroot/Translations/apl-catalogs.json) as the
// on-demand culture loader and loads the active language now — the rest are loaded on demand when the app selects
// one (see Home.razor), instant from the cache. English ships in code, so only an override is ever fetched.
await host.UseArchPillarLocalizationAsync();

await host.RunAsync();
