# Localization — DI, runtime, and interop

Setup and migration surface. None of this is touched when *adding* a string — it is wiring you do
once.

## Dependency injection

`AddArchPillarLocalization` (in `…Localization.DependencyInjection`) configures a single
`LocalizationContext` from `LocalizerOptions` and registers the native views — `ILocalizer`,
`ILocalizer<T>`, and the concrete `DefaultLocalizer`.

```csharp
builder.Services.AddArchPillarLocalization(new LocalizerOptions
{
    SourceCulture = "en",                 // language the in-code defaults are written in
    TranslationsDirectory = "Translations",
});
```

DI feeds the **process-wide ambient context**, so an injected `ILocalizer<T>` and a receiver-less
static `Translate(...)` resolve from the same catalogs — configure once, both worlds agree. Request
culture needs no extra wiring: localizers read `CurrentUICulture`, which
`app.UseRequestLocalization(...)` sets per request.

**`Localized<T>` bundles.** Chain `.AddArchPillarLocalizedBundles()` — the generator emits it
covering every bundle in the assembly, registering each (through its `ILocalizer<TSelf>`
constructor) as a singleton:

```csharp
builder.Services.AddArchPillarLocalization(options).AddArchPillarLocalizedBundles();
```

The extension is generated only when the project references the DI package, and is `internal`, so a
library that exposes bundles registers its own. A bundle used with DI must be `partial` so its
constructor can be generated — analyzer `APL0010` flags a non-`partial`, constructor-less bundle and
offers a one-click fix.

## The ambient store

One process-wide, layered store modeled on `IConfiguration`, reachable with no services. Read via
`Localizer.Default` (global category), `Localizer.For<T>()`, or the static `Translate` (with
`using static …Localizer;`). All configuration flows through one `LocalizerOptions` surface:

```csharp
Localizer.Configure(new LocalizerOptions { SourceCulture = "en", TranslationsDirectory = "Translations" });
Localizer.Initialize(options, eager: true);   // configure + load now (otherwise lazy on first use)
Localizer.AddCatalog(catalog);                 // layer a host override
Localizer.AddSource(new PseudoLocalizationSource()); // any ITranslationSource
Localizer.Reset();                              // clear to empty (test isolation)
```

Sources layer **embedded < satellite < directory < host**, last-wins; a lookup is one lock-free read
that falls to the in-code default on a miss.

## Isolated / context-based use

A process-wide static is not always wanted (parallel tests, multi-scope hosting). Construct a
**`LocalizationContext`** — the same environment the ambient facade wraps, as an ordinary object
that shares nothing with the ambient one or any other context:

```csharp
using var context = new LocalizationContext(new LocalizerOptions { SourceCulture = "en" });
context.AddCatalog(catalog);
string s = context.For<Checkout>().Translate("pay", "Pay now");
```

For just the resolution engine over a fixed catalog set, construct a **`DefaultLocalizer`**;
`DefaultLocalizer.FromCatalogs(...)` is the convenience for hosts with no file system (Blazor WASM:
fetch+parse catalogs over HTTP, hand them in). **Hot reload**: a `CatalogStore` with
`EnableHotReload = true` (debounced by `HotReloadDebounce`) reloads on file change, swapping an
immutable snapshot atomically so in-flight `Translate` calls never tear.

> **Testing:** the ambient store is global state. Call `Localizer.Reset()` between tests, or avoid
> the static entirely by constructing a `LocalizationContext` / `DefaultLocalizer` per test. See
> `docs/localization/recommendations.md`.

## IStringLocalizer interop and migration

For existing code, add `…Localization.StringLocalizer` and call `AddArchPillarStringLocalizer` (it
does the native registration **and** adds the adapters). It exposes the store as `IStringLocalizer`
/ `IStringLocalizer<T>` — name is the key, category is `typeof(T)`, positional args map to `{0}`,
`{1}`, … Crucially it **composes**: it registers the `.resx` factory and **falls through to it on an
ambient miss**, so existing `.resx` keeps resolving. Because this is the framework's single
`IStringLocalizerFactory` seam, MVC `IViewLocalizer`/`IHtmlLocalizer` and
`AddDataAnnotationsLocalization` route through it too.

```csharp
services.AddArchPillarStringLocalizer(new LocalizerOptions { SourceCulture = "en" });
```

Migration on-ramp (the interop package is meant to be dropped once you no longer need it):

- Existing translations keep working via the composing adapter.
- `IStringLocalizer` indexer **literals are extracted automatically** (on by default): the literal
  is key and default under `typeof(T)`. Only constant, valid-ICU literals are taken; a dynamic key
  or a `string.Format`-style literal (`"{0:C}"`) is skipped silently — a build never breaks.
- Mark anything else for extraction with **`L(...)`** without changing runtime behavior:

  ```csharp
  using static ArchPillar.Extensions.Localization.TranslationMarkers;
  throw new ArgumentException(L("Email is required"));
  ```

> `.resx` keys, a bare validator `ErrorMessage`, and view-localization calls are **not** extracted
> (no in-code default to harvest); the adapter still serves them at runtime.

## DataAnnotations and enum display

`[DisplayName]`, `[Display(Name=…)]`, `[Display(Description=…)]`, and `[Description]` carry real
display text, so the extractor lifts them **by default** (text-as-key, scoped to the declaring
type's category). Opt out with `ArchPillarLocalizationExtractAnnotations=false`.

For a **string-id** style instead of text-as-key, add an optional twin that carries just the
source default while the stable id stays in the system attribute (which the framework looks up):

```csharp
[Display(Name = "register.password.label")]   // the id the framework looks up = the catalog key
[LocalizedDisplayName("Password")]            // twin supplies the source default
public string Password { get; set; } = "";

[Required(ErrorMessage = "register.email.required")]
[LocalizedMessage<RequiredAttribute>("An email address is required.")]  // type arg names the validator
public string Email { get; set; } = "";
```

Twins: `[LocalizedDisplayName]` (for `[DisplayName]`/`[Display(Name)]`), `[LocalizedDescription]`
(for `[Description]`/`[Display(Description)]`), and generic `[LocalizedMessage<TValidation>]`.

**Enums** read their own annotation at runtime: `value.GetLocalizedDisplayName()` resolves the
member's `[Display(Name)]` value (key) — with a `[LocalizedDisplayName]` twin as the default —
under the enum's category. **MVC/Razor Pages** route DataAnnotations through the localizer with one
call (in `…Localization.AspNetCore`):

```csharp
builder.Services.AddControllersWithViews().AddArchPillarDataAnnotationsLocalization();
```

> Reading attributes is runtime reflection (inherent to attributes), the one place the library
> uses it on the consumer side. For Minimal APIs / Blazor validation, the .NET 11
> `IValidationLocalizer` seam is a separate follow-up; the MVC integration above needs none of it.
