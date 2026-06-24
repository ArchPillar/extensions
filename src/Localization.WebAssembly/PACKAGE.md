# ArchPillar.Extensions.Localization.WebAssembly

Blazor WebAssembly startup helper for
[ArchPillar.Extensions.Localization](https://www.nuget.org/packages/ArchPillar.Extensions.Localization).

A browser has no readable file system, so the directory catalog source finds nothing; a WebAssembly client
fetches its catalogs over HTTP from the app's static web assets instead. This package wires that up in one call.

```csharp
WebAssemblyHost host = builder.Build();

// Register the build-emitted manifest as a catalog provider and load the active language now.
await host.UseArchPillarLocalizationAsync();

await host.RunAsync();
```

`UseArchPillarLocalizationAsync` creates a `ManifestCatalogProvider` over the manifest (`apl-catalogs.json`,
emitted by the build), registers it on the ambient store with `Localizer.AddProvider`, and loads the active
culture with `Localizer.LoadCultureAsync`, so the first render is localized. Because the manifest is *registered* —
not loaded once and forgotten — another language is fetched on demand the moment it is needed:

```csharp
// When the app selects a different language, load its catalogs (instant from the PWA cache):
await Localizer.LoadCultureAsync(culture);
```

It uses the app's DI-registered `HttpClient` — the one the Blazor WebAssembly template registers over the host
base address — so the container owns the client and the provider reuses it for languages selected later. Loading
catalogs is all this does — the active culture is the app's concern.

## Re-rendering when a language is selected

Selecting a language is a normal Blazor event — do the load inside the handler, then set the culture:

```csharp
private async Task SetCulture(CultureInfo culture)
{
    await Localizer.LoadCultureAsync(culture);   // catalogs are in place before the next render
    CultureInfo.CurrentUICulture = culture;
}
```

Blazor re-renders the handler's component tree when it returns, with the translations already loaded — no base class, no special component.

For the rarer case where a catalog lands from a *background* fetch (a synchronous lookup of a culture nobody preloaded), the ambient store raises `Localizer.CatalogsChanged`. Blazor has no global "re-render the app" hook, so the refresh policy is the app's to choose — typically a cascading value at the app root, or a root component that refreshes. Subscribe there and marshal onto the renderer, since the event fires off the background-load thread:

```csharp
// in a root-level component (App/Layout), not per leaf component
protected override void OnInitialized() => Localizer.CatalogsChanged += OnCatalogsChanged;
public void Dispose() => Localizer.CatalogsChanged -= OnCatalogsChanged;
private void OnCatalogsChanged() => InvokeAsync(StateHasChanged);
```
