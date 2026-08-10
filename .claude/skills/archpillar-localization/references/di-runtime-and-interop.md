# Localization — DI, runtime, and interop

Setup and migration surface. None of this is touched when *adding* a string — it is wiring you do
once.

## Dependency injection

`AddArchPillarLocalization` (in `…Localization.DependencyInjection`) configures a single
`LocalizationContext` from `LocalizerOptions` and registers the native views — `ILocalizer`,
`ILocalizer<T>`, and `ILocalizerFactory`.

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

**Blazor WebAssembly.** A browser has no file system, so the directory source finds nothing; the
`…Localization.WebAssembly` package fetches catalogs over HTTP from the build-emitted manifest
instead (delivery details in `references/tooling-and-deployment.md`). Call it on the built host,
before `RunAsync`, passing the **same** `LocalizerOptions` used in DI — the call reconfigures the
ambient store from these options, so anything set only at the DI registration is silently dropped:

```csharp
var options = new LocalizerOptions { SourceCulture = "en" };
builder.Services.AddArchPillarLocalization(options);

WebAssemblyHost host = builder.Build();
await host.UseArchPillarLocalizationAsync(options);
await host.RunAsync();
```

It loads the active language up front, so the first render is localized. A language switch loads the
new culture and then sets it — `await Localizer.LoadCultureAsync(culture);` followed by
`CultureInfo.CurrentUICulture = culture;` — with no restart and no `HttpClient` to thread through
components.

## The ambient store

One process-wide, layered store modeled on `IConfiguration`, reachable with no services. Read via
`Localizer.Default` (global category), `Localizer.For<T>()`, or the static `Translate` (with
`using static …Localizer;`). All configuration flows through one `LocalizerOptions` surface:

```csharp
var options = new LocalizerOptions { SourceCulture = "en", TranslationsDirectory = "Translations" };
Localizer.Initialize(options);                // configure now, load lazily on first use
Localizer.Initialize(options, eager: true);   // configure + load now (otherwise lazy on first use)

// Layer a host override: no runtime mutation surface — build new options with an extra provider
// factory appended to Providers and reconfigure the ambient context.
Localizer.Ambient.Configure(options with { Providers = [.. options.Providers, _ => new InMemoryCatalogProvider([catalog])] });
Localizer.Ambient.Reset();                     // clear to empty (test isolation)
```

Sources layer **embedded < satellite < directory < host**, last-wins; a lookup is one lock-free read
that falls to the in-code default on a miss.

## Isolated / context-based use

A process-wide static is not always wanted (parallel tests, multi-scope hosting). Construct a
**`LocalizationContext`** — the same environment the ambient facade wraps, as an ordinary object
that shares nothing with the ambient one or any other context:

```csharp
var options = new LocalizerOptions { SourceCulture = "en" };
using var context = new LocalizationContext(options);
context.Configure(options with { Providers = [.. options.Providers, _ => new InMemoryCatalogProvider([catalog])] });
var s = context.For<Checkout>().Translate("pay", "Pay now");
```

For a fixed catalog set with no file system (a test that wants no disk I/O; a Blazor WASM client uses
the `…WebAssembly` package above instead), layer an `InMemoryCatalogProvider` into the context through
`Providers` — there is no lower-level "bare engine" door; `DefaultLocalizer` is `internal`, built only
by a `LocalizationContext` (or the ambient `Localizer`) over its own store. **Hot reload**: `EnableHotReload` (debounced by `HotReloadDebounce`)
reloads on file change, swapping an immutable snapshot atomically so in-flight `Translate` calls never tear.

> **Testing:** the ambient store is global state. Call `Localizer.Ambient.Reset()` between tests, or avoid
> the static entirely by constructing a `LocalizationContext` per test. See
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

## `[Localized]`, DataAnnotations, and enum display

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

**`[Localized]` — key and default in one attribute.** When nothing but this library reads the
annotation, skip the system-attribute-plus-twin pair: `[Localized(key, default)]` carries both on
one line. A description (a form-field hint, a help line) is optional, and its key is **derived** as
the display key + `.description` unless `DescriptionKey` overrides it — deliberately *not*
text-as-key, so editing the hint does not orphan its translations:

```csharp
public sealed class RegisterModel
{
    [Localized("user.email", "Email address", Description = "We never share it.")]
    public string Email { get; set; } = "";
}
```

Reach for `[Display]` (with a twin when you want a string id) only when something **other than this
library** must also read the annotation — the framework's own `Order`/`GroupName`/`Prompt`, or a
consumer that looks for `DisplayAttribute` specifically. Both forms extract identically and resolve
under the declaring type's category, so one model can mix them.

**Reading an annotation at runtime.** For the consumers ASP.NET's DataAnnotations pipeline does not
reach (Blazor, a console renderer, a generic form renderer), the `MemberInfo` helpers resolve the
annotation — `[Localized]` first, else the system attribute plus its twin — under the declaring
type's category; a member with no annotation renders as its own name, so callers never null-check:

```csharp
PropertyInfo email = typeof(RegisterModel).GetProperty(nameof(RegisterModel.Email))!;
var label = email.GetLocalizedDisplayName();
var hint  = email.GetLocalizedDescription();

// The expression forms, so you never reach for GetProperty yourself:
var typed    = MemberLocalizationExtensions.GetLocalizedDisplayName<RegisterModel>(x => x.Email);
var typeFree = MemberLocalizationExtensions.GetLocalizedDisplayName(() => model.Email);
```

A `Type` is itself a `MemberInfo`, so `typeof(RegisterModel).GetLocalizedDisplayName()` labels the
model; every helper has a `LocalizationContext` overload for isolated resolution (tests,
multi-tenant hosting).

**Enums** read their own annotation at runtime: `value.GetLocalizedDisplayName()` resolves the
member's `[Display(Name)]` value (key) — with a `[LocalizedDisplayName]` twin as the default —
under the enum's category. **MVC/Razor Pages** route DataAnnotations through the localizer with one
call (in `…Localization.AspNetCore`):

```csharp
builder.Services.AddControllersWithViews().AddArchPillarDataAnnotationsLocalization();
```

The same call also teaches MVC to read `[Localized]` (via a display-metadata provider) — the
DataAnnotations seam alone only translates strings MVC already found on a system attribute, so
without it a member carrying just `[Localized]` would fall back to its property name in views.

> Reading attributes is runtime reflection (inherent to attributes), the one place the library
> uses it on the consumer side. For Minimal APIs / Blazor validation, the .NET 11
> `IValidationLocalizer` seam is a separate follow-up; the MVC integration above needs none of it.
