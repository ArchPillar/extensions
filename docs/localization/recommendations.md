# Recommendations

Production patterns and the non-obvious rules. Each is an imperative with the reasoning behind it; the
ordering and trimming items in particular are easy to get wrong and hard to debug after the fact.

## Use `AddArchPillarStringLocalizer` to migrate an `IStringLocalizer` / `.resx` app

When adopting the library next to existing `IStringLocalizer` / `.resx` code, add the
`ArchPillar.Extensions.Localization.StringLocalizer` package and call `AddArchPillarStringLocalizer`. It
registers the native views **and** an `IStringLocalizer` adapter that composes over your existing
`ResourceManager` factory — so your `.resx` keeps resolving on an ambient miss. It also registers that
factory itself (via `AddLocalization()`, which no-ops when you have already called it), so **call order no
longer matters**: your `.resx` survives whether you register the framework's localization before or after.

```csharp
services.AddLocalization(options => options.ResourcesPath = "Resources"); // your existing setup (any order)
services.AddArchPillarStringLocalizer();                                  // composes over it
```

> Once your code no longer depends on `IStringLocalizer`, drop the package and call
> `AddArchPillarLocalization` (native registration only) instead — the migration on-ramp is meant to be
> removed.

## Set `SourceCulture` to the language your defaults are written in

The in-code defaults are source-language text and are rendered with `SourceCulture`'s rules (so an
English default pluralizes by English rules even under a Japanese request). It defaults to `en`; if
your defaults are written in another language, say so — otherwise plurals and number formats render
under the wrong rules.

```csharp
services.AddArchPillarLocalization(new LocalizerOptions { SourceCulture = "de" });
```

## Always include an `other` branch in `plural` and `select`

ICU requires it, and the analyzer enforces it (`APL0005`). The `other` branch is the catch-all every
plural category can fall back to; without it a count with no matching selector has nothing to render.

```csharp
"{count, plural, one {# item} other {# items}}" // not just `one {…}`
```

## Prefer files for trimming, single-file, and AOT

The files-on-disk path uses loose catalogs parsed with DOM APIs and no reflection over assemblies, so
it is safe under every publish mode. The opt-in **embedded** path behaves differently — validated on
.NET 10 / linux-x64 (reproduced by `Localization.TrimSample`):

| Publish mode | Main-assembly embed (`[LocalizationCatalog]`) | Culture satellite |
|---|---|---|
| Full trim (`PublishTrimmed`, `TrimMode=full`) | works, no trimmer roots | preserved |
| Single-file + full trim | works | bundled into the exe |
| NativeAOT (`PublishAot`) | works | **does not load** — degrades to the in-code default |

NativeAOT cannot load a separate managed satellite assembly, so under AOT the satellite lookup fails
*safely* (it falls back to the in-code default, never crashes). **For AOT, prefer files (a merged
bundle per culture) or a main-assembly embedded catalog; avoid satellite embedding.**
`Localization.AotSample` shows the AOT-safe shape; `Localization.TrimSample` is the broader validation.

> An app published with `InvariantGlobalization=true` cannot select a non-default culture, so it can
> never load non-default translations — standard .NET advice, not specific to this library.

## Keep `AssemblyName` equal to `RootNamespace` when using `.resx`

`ResourceManager` derives a resource's name from the assembly's root namespace; when the assembly name
differs, it fails to find the `.resx`. If you mix the composing adapter with real `.resx` resources,
pin the two equal (or apply `[assembly: RootNamespace]`) so the legacy resources resolve.

```xml
<RootNamespace>MyApp</RootNamespace>
<AssemblyName>MyApp</AssemblyName>
```

## Prefer an isolated context (or localizer) in tests over the shared ambient store

The ambient store is process-wide global state, so tests that touch it share it and cannot run in
parallel safely. To avoid that, **construct a `LocalizationContext`** (or, for just the engine, a
`DefaultLocalizer`) per test and read through it — it shares nothing with the ambient store or with another
context, so tests stay isolated and parallelisable with no teardown.

Reserve the shared ambient store (with `Localizer.Reset()` in setup/teardown) for the handful of tests
that specifically cover ambient loading and discovery.

```csharp
using var context = new LocalizationContext(new LocalizerOptions { SourceCulture = "en" });
context.AddCatalog(catalog);                    // isolated — no shared state, safe in parallel

var localizer = new DefaultLocalizer(catalogs); // or just the engine over fixed catalogs
```

## Treat `IStringLocalizer` extraction as a bridge, not a source

The adapter serves `IStringLocalizer` call sites, `.resx` keys, DataAnnotations messages, and view
localization at runtime, but only **constant, valid-ICU indexer literals** are extracted into the
catalog — `.resx` keys and a validator's `ErrorMessage` are not (they have no in-code default to
harvest). Display **annotations** are the exception: `[DisplayName]` / `[Display]` / `[Description]` are
extracted by the dedicated annotation pass (next), so they need no bridge. Anything else you want
round-tripped to translators must go through the native `ILocalizer` API, an extracted indexer literal,
or an `L(...)` marker. Migrate hot paths to the native API over time.

## Let display annotations extract themselves; add a twin only for string-id keys

`[DisplayName]`, `[Display(Name/Description)]`, and `[Description]` are extracted by default (the literal
is both key and default, scoped to the declaring type), so annotated models and enums reach translators
with no extra work — there is no reason to restate those strings through `Translate(...)`. Opt a project
out with `ArchPillarLocalizationExtractAnnotations=false` only when you deliberately do not want
annotations localized.

The literal-as-key default is fine until a wording change copy-edits the key out from under its
translations. If you would rather key by a **stable string id**, put the id in the system attribute — the
value the framework looks up — and add a `[Localized…]` twin carrying the source default for that id. The
twin is purely the default; it never holds the key. For validation, the id goes in the validator's
`ErrorMessage` and `[LocalizedMessage<TValidation>]` carries the default.

```csharp
[Display(Name = "checkout.continueToPayment")]                  // the string id is the key
[LocalizedDisplayName("Save changes and continue to payment")]  // the twin supplies the source default
public string Continue { get; set; } = "";
```

> Localize MVC DataAnnotations with `AddArchPillarDataAnnotationsLocalization()` (the `…AspNetCore`
> package), and enums with `value.GetLocalizedDisplayName()`. Both resolve under the declaring type's
> category, so a translation added for one is the same entry the other reads.

## Register the localizer as a singleton

The `DefaultLocalizer` and the ambient store are built for concurrent use and cache their snapshot;
the DI registration is a singleton for this reason. Do not construct a `CatalogStore` per request —
that re-parses catalogs and discards the cached snapshot on every call.

## Reference the package directly in any project that authors localized strings

Build-time extraction is per-authoring-assembly, so it runs only where `ArchPillar.Extensions.Localization`
is referenced **directly** — the project that actually writes the strings. A project that picks the
package up only *transitively* (through a library that uses it) has no strings of its own, so it is
skipped, and its build doesn't pay the extraction cost. Publish-time merging still runs everywhere, so a
deployed app always bundles the catalogs of every localized library in its graph.

If a project authors strings but sees the package only transitively — or wires the build assets in by
hand (e.g. from `Directory.Build.props`) — add a direct `<PackageReference>`, or opt in explicitly:

```xml
<PropertyGroup>
  <ArchPillarLocalizationExtractTransitively>true</ArchPillarLocalizationExtractTransitively>
</PropertyGroup>
```

## Load catalogs over HTTP in Blazor WebAssembly — there is no file system

A browser has no readable file system, so the directory source finds nothing: a WebAssembly client must fetch
its catalogs over HTTP from the app's static web assets. Use `AddCatalogsFromManifestAsync`. When the package
is referenced in a Blazor WebAssembly app, the build generates `apl-catalogs.json` and registers it as a static
web asset through the Razor pipeline — gathering the app's own catalogs *and every referenced localized
library's* (merged into one bundle per culture on publish) — so the loader discovers what to fetch with no
hand-kept file list and nothing committed to the source tree. Call it before `RunAsync` so the first render is
already localized.

```csharp
using var http = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
await Localizer.AddCatalogsFromManifestAsync(http);

await builder.Build().RunAsync();
```

A missing manifest, a missing catalog, or a malformed one is skipped, so a partial deployment degrades to the
in-code defaults rather than throwing. When you would rather name the catalogs than discover them, the
primitive `AddCatalogsFromHttpAsync(http, ["Translations/App.de.arb"])` fetches exactly the URIs you pass.

## Serve `.arb` catalogs from ASP.NET Core — register the content type

The static file middleware returns 404 for an unknown file extension by default, so an ASP.NET Core host
serving a WebAssembly client's catalogs 404s every `.arb` (and `.xliff` / `.po`) request — and the client
silently falls back to its in-code defaults. Add the `ArchPillar.Extensions.Localization.AspNetCore` package
and register the catalog content types:

```csharp
app.UseArchPillarTranslationFiles();          // serve the catalog formats from the web root
app.UseArchPillarTranslationFiles("/party");  // ...or scoped under a request path
```

When the app already configures `UseStaticFiles`, register the types on its content-type provider instead —
handy when several WebAssembly clients are hosted under different paths from one provider:

```csharp
var provider = new FileExtensionContentTypeProvider().AddArchPillarTranslationFormats();
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = provider });
app.UseStaticFiles(new StaticFileOptions { RequestPath = "/party", ContentTypeProvider = provider });
```

## Ship a Razor library's catalogs as files, not satellites, for WebAssembly consumers

A library can ship its catalogs two ways: as **loose files** (the default — copied to output / served as static
web assets) or as **embedded culture satellites** (`ArchPillarLocalizationEmbedTargets=true`). For a library
consumed by a **Blazor WebAssembly** app, prefer files. The WebAssembly build gathers every referenced library's
*file* catalogs into the app's manifest and merges them into one bundle per culture, so the client fetches a
single file per language. Satellite-embedded catalogs ride in per-culture assemblies instead — they still load
if the runtime downloads the satellite, but they can't be gathered into the HTTP manifest or folded into the
merged bundle, and AOT can't load a satellite at all (see the trimming/AOT note above). Use a **Razor class
library** (`Microsoft.NET.Sdk.Razor`) so the library participates in the static web asset pipeline; a plain
class library has no way to serve its catalogs over HTTP.
