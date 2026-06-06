# Integration — dependency injection & IStringLocalizer

`ArchPillar.Extensions.Localization.DependencyInjection` is the **optional** adapter package (Decision
D-5). The core runtime stays dependency-free; reference this package only when you want DI registration
or `IStringLocalizer` interop.

## Registration

```csharp
services.AddArchPillarLocalization(new LocalizerOptions
{
    TranslationsDirectory = "Translations",
    SourceCulture = "en"
});
```

This registers a singleton `Localizer` and adapts it to `IStringLocalizer`, `IStringLocalizer<T>`, and
`IStringLocalizerFactory`. Inject whichever you prefer:

```csharp
public sealed class HomeModel(IStringLocalizer<HomeModel> localizer)
{
    public string Title => localizer["home.title"];          // override for the request culture, else "home.title"
    public string Inbox(int n) => localizer["inbox.count", n]; // positional arg maps to {0}
}

// or use the Localizer directly for the full key + in-code default + named arguments:
public sealed class Greeter(Localizer localizer)
{
    public string Greet(string name) =>
        localizer.Translate("home.greeting", "Hello {name}", ("name", name));
}
```

## The IStringLocalizer model

`IStringLocalizer` has no separate key vs. in-code default, so the adapter follows its standard model:

- **The name is the key.** `localizer["home.title"]` looks up the override for the current culture.
- **Missing → name.** With no override, it returns the name with `ResourceNotFound = true`.
- **Positional arguments map to ICU `{0}`, `{1}`, …** — so `IStringLocalizer`-authored messages use
  positional placeholders. (Use the `Localizer` directly for named placeholders and an in-code default.)
- The resource type of `IStringLocalizer<T>` is ignored: keys are one global symbolic namespace.

## ASP.NET Core

No extra wiring is needed for request culture. `Localizer` reads `CultureInfo.CurrentUICulture`, which
`app.UseRequestLocalization(...)` sets per request — so the standard ASP.NET culture middleware drives
which override is used.

## Hosts without a file system (Blazor WebAssembly)

`AddArchPillarLocalization(LocalizerOptions)` loads catalogs from a directory, which works wherever there
is a readable file system (console, ASP.NET, Blazor Server). Blazor WebAssembly runs in the browser
sandbox and has no such directory — the `.arb`/`.xliff`/`.po` files are static web assets fetched over
HTTP. For that case, fetch and parse the catalogs yourself, then build the localizer from them:

```csharp
using var http = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
var catalogs = new List<Catalog>();
await using (Stream de = await http.GetStreamAsync("Translations/de.arb"))
{
    catalogs.Add(await new ArbTranslationFormat().ReadAsync(de, CancellationToken.None));
}

builder.Services.AddArchPillarLocalization(
    Localizer.FromCatalogs(catalogs, new LocalizerOptions { SourceCulture = "en" }));
```

`Localizer.FromCatalogs` builds the same snapshot as the directory loader (source-culture catalog and
untranslated entries skipped, later catalogs winning on overlap) without reading any directory, and the
`AddArchPillarLocalization(Localizer)` overload registers that instance with the `IStringLocalizer`
adapters. The format providers (`ArbTranslationFormat`, `XliffTranslationFormat`, `PoTranslationFormat`)
are public and stream-based, so the same approach works for any embedded or remote catalog source.

## Samples

Four runnable samples under `samples/Localization` show the same registration across hosting models:

- **`Localization.ConsoleSample`** — a generic `Host` resolving the `Localizer` from DI (named arguments,
  ICU plurals, in-code English default, German override).
- **`Localization.AspNetSample`** — a minimal API with `UseRequestLocalization`; one endpoint injects the
  `Localizer`, another the `IStringLocalizer<T>` adapter. Switch culture with `?culture=de`.
- **`Localization.BlazorSample`** — a server-rendered Razor component injecting both `Localizer` and
  `IStringLocalizer<Home>`, with culture-switch links. It also shows the honest "missing → name" behaviour
  for the source language under the `IStringLocalizer` model.
- **`Localization.WasmSample`** — standalone Blazor WebAssembly: the German catalog is fetched over HTTP
  and handed to `Localizer.FromCatalogs`, and the culture switches in-process (no reload) because
  WebAssembly is single-threaded and the `Localizer` reads `CurrentUICulture` on each call.
