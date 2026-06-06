using ArchPillar.Extensions.Localization;
using ArchPillar.Extensions.Localization.Formats;
using Localization.WasmSample;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Blazor WebAssembly has no readable file system, so the directory loader cannot be used. Instead the
// catalogs are fetched over HTTP from the app's static web assets and parsed with the ARB provider, then
// handed to Localizer.FromCatalogs. English ships in code, so only the German override is fetched.
using var http = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
var catalogs = new List<Catalog>();
await using (Stream de = await http.GetStreamAsync("Translations/de.arb"))
{
    catalogs.Add(await new ArbTranslationFormat().ReadAsync(de, CancellationToken.None));
}

builder.Services.AddArchPillarLocalization(
    Localizer.FromCatalogs(catalogs, new LocalizerOptions { SourceCulture = "en" }));

await builder.Build().RunAsync();
