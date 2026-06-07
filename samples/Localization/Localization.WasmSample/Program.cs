using ArchPillar.Extensions.Localization;
using ArchPillar.Extensions.Localization.Formats;
using Localization.WasmSample;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Ambient = ArchPillar.Extensions.Localization.Localization;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Blazor WebAssembly has no readable file system, so the directory source finds nothing. Instead the
// catalogs are fetched over HTTP from the app's static web assets, parsed with the ARB provider, and fed
// into the ambient store; English ships in code, so only the German override is fetched. AddArchPillar-
// Localization then registers the injectable views over that same ambient store.
using var http = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
await using (Stream de = await http.GetStreamAsync("Translations/de.arb"))
{
    Ambient.AddCatalog(await new ArbTranslationFormat().ReadAsync(de, CancellationToken.None));
}

builder.Services.AddArchPillarLocalization(new LocalizerOptions { SourceCulture = "en" });

await builder.Build().RunAsync();
