using ArchPillar.Extensions.Localization;
using ArchPillar.Extensions.Localization.Formats;
using Localization.WasmSample;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Ambient = ArchPillar.Extensions.Localization.Localizer;

// ---------------------------------------------------------------------------
// Localization.WasmSample
//
// Demonstrates ArchPillar.Extensions.Localization in a Blazor WebAssembly client:
//   - Fetching an ARB catalog over HTTP from static web assets (no file system in the browser)
//   - Injecting ILocalizer and IStringLocalizer<T> into components over the ambient store
//   - Switching culture in code at runtime with a button — no reload, no request middleware
//   - A missing override surfacing via IStringLocalizer's ResourceNotFound (the key shows through)
//
// The UI lives in Pages/Home.razor; the German catalog is wwwroot/Translations/Localization.WasmSample.de.arb.
// ---------------------------------------------------------------------------
WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Blazor WebAssembly has no readable file system, so the directory source finds nothing. Instead the
// catalogs are fetched over HTTP from the app's static web assets, parsed with the ARB provider, and fed
// into the ambient store; English ships in code, so only the German override is fetched. AddArchPillar-
// StringLocalizer then registers the native views and the IStringLocalizer adapter over that same store.
using var http = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
await using (Stream de = await http.GetStreamAsync("Translations/Localization.WasmSample.de.arb"))
{
    Ambient.AddCatalog(await new ArbTranslationFormat().ReadAsync(de, CancellationToken.None));
}

builder.Services.AddArchPillarStringLocalizer(new LocalizerOptions { SourceCulture = "en" });

await builder.Build().RunAsync();
