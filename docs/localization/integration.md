# Integration — dependency injection & IStringLocalizer

`ArchPillar.Extensions.Localization.DependencyInjection` is the **optional** adapter package. The core
runtime needs no DI — translations live in the ambient store and work with no services (see
[`runtime.md`](runtime.md)). Reference this package when you want injectable localizers or `IStringLocalizer`
interop. DI is an **escape hatch over the same ambient store** (Decision **D-I**), never a parallel system.

## Registration

```csharp
services.AddArchPillarLocalization(new LocalizerOptions
{
    TranslationsDirectory = "Translations",
    SourceCulture = "en"
});
```

This feeds the ambient store from the options (so an injected localizer, a non-DI caller, and an exception
text all read one source) and registers:

- `ILocalizer` and `ILocalizer<T>` — the native API, over the ambient store;
- `IStringLocalizer`, `IStringLocalizer<T>`, `IStringLocalizerFactory` — the interop adapters;
- a concrete `Localizer` for direct injection.

Inject whichever you prefer:

```csharp
// Native API — full key + in-code default + named arguments, category = typeof(T):
public sealed class HomeModel(ILocalizer<HomeModel> localizer)
{
    public string Title => localizer.Translate("title", "Home");
    public string Greet(string name) => localizer.Translate("greeting", "Hello {name}", ("name", name));
}

// IStringLocalizer interop — the name is the key:
public sealed class LegacyModel(IStringLocalizer<LegacyModel> localizer)
{
    public string Title => localizer["Home"];
    public string Inbox(int n) => localizer["You have {0}", n]; // positional arg → {0}
}
```

No extra wiring is needed for request culture: the localizers read `CultureInfo.CurrentUICulture`, which
`app.UseRequestLocalization(...)` sets per request, so the standard ASP.NET culture middleware drives which
override is used.

## The IStringLocalizer model

`IStringLocalizer` has no separate key vs. in-code default, so the adapter follows its standard model, with
one important addition — **it composes**:

- **The name is the key.** `localizer["Home"]` looks up the override for the current culture.
- **Category = `typeof(T)`.** `IStringLocalizer<T>` resolves under `T`'s full name (it flows through the BCL
  `StringLocalizer<T>` → `factory.Create(typeof(T))`), matching how `IStringLocalizer<T>` call sites are
  extracted. The non-generic `IStringLocalizer` uses the global category.
- **Positional arguments map to ICU `{0}`, `{1}`, …** — so `IStringLocalizer`-authored messages use
  positional placeholders. (Use `ILocalizer` for named placeholders and an explicit in-code default.)
- **It composes over a previously-registered factory** (Decision **D-J**). If you registered
  `AddLocalization()` (the ResourceManager/`.resx` factory) *before* `AddArchPillarLocalization()`, the
  adapter tries the ambient store first and, on a miss, **falls through to that factory** so your existing
  `.resx` keeps resolving. Only when neither has the key does it return the name (with `ResourceNotFound`).

## Migrating from IStringLocalizer / .resx

Adopting this library next to an existing `IStringLocalizer` + `.resx` codebase costs almost nothing — three
deliberate concessions (Decision **D-J**):

1. **Existing translations keep working.** The composing adapter (above) means you can register
   `AddArchPillarLocalization()` after your `AddLocalization()` and your `.resx` still resolves; ArchPillar's
   store layers on top and wins where it has an entry. No call sites change. See
   `samples/Localization/Localization.MigrationSample`.

2. **`IStringLocalizer` call sites are extracted automatically.** The extractor recognizes the indexer
   (`loc["…"]`) and lifts the literal into the catalog — the literal is both the key and the default, under
   `typeof(T)`'s category. This is **on by default** and deliberately conservative: only a **constant,
   valid-ICU** literal is extracted; a dynamic key (`loc[someVar]`) or a `string.Format`-style literal
   (`loc["Price: {0:C}"]`, which isn't ICU) is **skipped silently**, so it never breaks a build.

3. **`L(...)` marks anything else.** For a string that never flows through a localizer — a log line, a
   `throw new(...)` message — import the marker and wrap the literal:

   ```csharp
   using static ArchPillar.Extensions.Localization.TranslationMarkers;

   throw new ArgumentException(L("Email is required"));
   ```

   `L` returns its argument unchanged at runtime (no setup, no lookup); its only job is to make the literal
   extractable (key = default = the text, global category). Convert the call to the native `ILocalizer` API
   when you want the site to actually resolve overrides.

### What is *not* extracted

`IStringLocalizer` is a runtime-resolution seam, not a compile-time source of truth, so the parts of it that
have **no in-code default and no attribute** cannot be harvested:

- **`.resx` keys** — the key *is* the lookup and the fallback is the key string; there is nothing at the call
  site to extract.
- **DataAnnotations and view localization** (`[Required(ErrorMessage = …)]`, `@Localizer["…"]`) — these
  resolve *through* `IStringLocalizer` at runtime but carry no `[Translatable]` marker the extractor can see.

The interop adapter still serves all of these at runtime (it's a one-way bridge: read-at-runtime, not a
source for the extraction pipeline). Anything you want round-tripped to translators must go through the
native `ILocalizer` API, the extracted `IStringLocalizer` indexer literals above, or an `L(...)` marker.

## Hosts without a file system (Blazor WebAssembly)

Loading from a directory works wherever there is a readable file system (console, ASP.NET, Blazor Server).
Blazor WebAssembly runs in the browser sandbox with no such directory — the `.arb`/`.xliff`/`.po` files are
static web assets fetched over HTTP. Fetch and parse the catalogs yourself, then layer them into the store:

```csharp
using var http = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
await using (Stream de = await http.GetStreamAsync("Translations/de.arb"))
{
    Localization.AddCatalog(await new ArbTranslationFormat().ReadAsync(de, CancellationToken.None));
}

builder.Services.AddArchPillarLocalization(new LocalizerOptions { SourceCulture = "en" });
```

The format providers (`ArbTranslationFormat`, `XliffTranslationFormat`, `PoTranslationFormat`) are public and
stream-based, so the same approach loads any embedded or remote catalog source — including a third-party API
or a database (the catalog loading surface is intentionally open).

## Samples

Runnable samples under `samples/Localization`:

- **`Localization.ConsoleSample`** — a generic `Host` resolving `ILocalizer` from DI (named arguments, ICU
  plurals, in-code English default, German override).
- **`Localization.AspNetSample`** — a minimal API with `UseRequestLocalization`; endpoints inject the native
  localizer and the `IStringLocalizer<T>` adapter. Switch culture with `?culture=de`.
- **`Localization.BlazorSample`** — a server-rendered Razor component injecting both, with culture-switch
  links.
- **`Localization.WasmSample`** — standalone Blazor WebAssembly: the German catalog is fetched over HTTP and
  layered into the ambient store; culture switches in-process.
- **`Localization.GreetingLibrary` + `Localization.LibraryConsumer`** — a no-DI, batteries-included library
  that ships German embedded as a satellite, consumed with **zero** configuration, including a localized
  exception message thrown with no services.
- **`Localization.MigrationSample`** — migrating an existing `AddLocalization()` + `.resx` app: the composing
  adapter keeps the legacy `.resx` resolving while ArchPillar's `de.arb` wins where it has an entry, plus an
  `L(...)` marker. No call-site changes.
- **`Localization.TrimSample`** — validates the embedded/satellite path under trimming / single-file / AOT
  (see [`runtime.md`](runtime.md) → Publishing).
