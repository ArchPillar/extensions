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

## Reset the ambient store between tests, and test functionality with explicit catalogs

The ambient store is process-wide global state, so tests share it. Call `Localizer.Reset()` in
setup/teardown for determinism. Test most behavior with an **isolated** `DefaultLocalizer` over explicit
catalogs; reserve the ambient store for the handful of tests that specifically cover ambient loading.

```csharp
Localizer.Reset();
var localizer = new DefaultLocalizer(catalogs); // isolated — no shared state
```

## Treat `IStringLocalizer` extraction as a bridge, not a source

The adapter serves `IStringLocalizer` call sites, `.resx` keys, DataAnnotations messages, and view
localization at runtime, but only **constant, valid-ICU indexer literals** are extracted into the
catalog — `.resx` keys and attribute messages are not (they have no in-code default to harvest).
Anything you want round-tripped to translators must go through the native `ILocalizer` API, an
extracted indexer literal, or an `L(...)` marker. Migrate hot paths to the native API over time.

## Register the localizer as a singleton

The `DefaultLocalizer` and the ambient store are built for concurrent use and cache their snapshot;
the DI registration is a singleton for this reason. Do not construct a `CatalogStore` per request —
that re-parses catalogs and discards the cached snapshot on every call.
