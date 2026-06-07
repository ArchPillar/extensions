# Runtime — usage

`ArchPillar.Extensions.Localization` is the package an application references. It loads translation
catalogs as **overrides** and renders translatable call sites; the in-code default is always the
source of truth for the source language and the terminal fallback for every other language, so an
app with no translation files still runs correctly.

> Design reference: [`05-runtime.md`](05-runtime.md) and Decisions **D-H** (categories) / **D-I**
> (the ambient store) in [`DECISIONS.md`](DECISIONS.md). All three container-format providers — ARB,
> XLIFF 2.1, and Portable Object — are bundled into this package; there are no separate `Formats.*`
> packages.

## The ambient store (the primary model)

Translations live in **one process-wide, layered store**, modeled on `IConfiguration`. It is reachable
with no services and no setup, so a string can be localized from anywhere — including an **exception
message thrown before any container exists**. You never construct it; you read it through a localizer:

```csharp
using ArchPillar.Extensions.Localization;

// Global (uncategorized) localizer:
string title = Localization.Default.Translate("home.title", "Home");

// Category-scoped localizer (see "Categories" below):
ILocalizer<HomeModel> loc = Localization.For<HomeModel>();
string greeting = loc.Translate("greeting", "Hello {name}", ("name", "Ada"));
```

`Translate(key, default, …)` looks up the loaded override for `CultureInfo.CurrentUICulture` and the key,
falling back through parent cultures to the **in-code default** supplied right there at the call site. The
default is never stored — it is the floor of every lookup.

A library needs no configuration for its translations to work: it ships them embedded or as files, and the
store discovers them lazily as assemblies load. A host overrides any of them by layering a later source.

### Categories — the `ILogger<T>` model

There are no user-managed namespaces. A key is implicitly scoped by a **category**, exactly as `ILogger<T>`
scopes log entries: the category of `ILocalizer<T>` is the **full type name of `T`**. Inject
`ILocalizer<MyComponent>` and its keys live under `MyComponent`, collision-free from another component's
identical key. Shared strings live in their own type used as the scope (`ILocalizer<SharedStrings>`), which
doubles as ordinary code reuse.

```csharp
public sealed class HomeModel(ILocalizer<HomeModel>? localizer = null)
{
    // Default to the ambient store, so a no-DI caller can just `new HomeModel()`.
    private readonly ILocalizer<HomeModel> _localizer = localizer ?? Localization.For<HomeModel>();

    public string Title => _localizer.Translate("title", "Home");
}
```

`Localized<TSelf>` is an optional base for a bundle of strings where the **member name is the key** (via
`[CallerMemberName]`) and the deriving type is the category — so neither is repeated:

```csharp
public sealed class ButtonLabels(ILocalizer<ButtonLabels> loc) : Localized<ButtonLabels>(loc)
{
    public string Save   => Translate("Save");
    public string Cancel => Translate("Cancel");
}
```

### Configuring and overriding the store

```csharp
Localization.SourceCulture = "en";                 // language the in-code defaults are written in
Localization.TranslationsDirectory = "Translations"; // where loose catalog files are read from
Localization.AddCatalog(catalog);                  // layer a host override (a later source wins)
Localization.AddSource(new PseudoLocalizationSource()); // layer a dynamic source (e.g. pseudo-loc)
```

Sources are layered **embedded < satellite < directory < host**, last-wins, and a lookup is one
priority-ordered, lock-free read. `Localization.Reset()` clears everything (intended for test isolation
against the shared store).

## Isolated localizers

For tests or multi-tenant scenarios you can construct a `Localizer` that bypasses the ambient store entirely
and reads only the catalogs you hand it:

```csharp
using var localizer = new Localizer(catalogs, new LocalizerOptions { SourceCulture = "en" });
localizer.Translate(CultureInfo.GetCultureInfo("de"),
    "home.greeting", "Hello {name}", context: null, ("name", "Ada"));
```

`Localizer.FromCatalogs(...)` is the same thing for hosts without a readable file system (the catalogs are
fetched and parsed first — see the Blazor WebAssembly note in [`integration.md`](integration.md)). It is
also what `dotnet apl merge` and the DI layer build on.

## Resolution order

For `(culture, key, context)` the localizer tries, in order:

1. the override for the **exact** culture,
2. each **parent** culture (`de-AT` → `de` → invariant),
3. the **in-code default** supplied at the call site.

An **override** is formatted against the **requested culture** (a German override pluralizes by German
rules even though the key was authored in another language). The **in-code default** is formatted
against the **`SourceCulture`** instead — it is source-language text, so it must pluralize by the rules
of the language it was written in, not by whatever culture happened to be requested. The defaults are
never assumed to be English; `SourceCulture` (default `en`, fully configurable) declares the language
they are authored in. A missing snapshot, culture, or key never fails — it degrades to the default for
that one call.

## Loading — files, embedded, and satellites

A catalog reaches the store one of three ways. **Files-on-disk is the default** and the one path that works
everywhere (plain, trimmed, single-file, AOT):

- **Files (default).** Each referencing library's catalogs are copied to the output as
  `Translations/<AssemblyName>.<culture>.<ext>`, and the ambient store loads `TranslationsDirectory`
  (default `Translations` beside the binary) on first use. Loose files, parsed with `JsonDocument` /
  `XDocument` — no reflection over assemblies, so nothing for a trimmer to remove.
- **Embedded — opt-in** (`ArchPillarLocalizationEmbedTargets=true`). Catalogs become **satellite
  assemblies** (`<name>.<culture>.<ext>` → `de/<AssemblyName>.resources.dll`), discovered lazily per
  requested culture via `Assembly.GetSatelliteAssembly`, advertised by an assembly attribute so discovery is
  an attribute read, not a resource scan. A culture-neutral or merged catalog can instead ride in the main
  assembly via `[LocalizationCatalog]`. This leans into .NET's existing satellite convention.

Within either path: formats may be mixed (higher-fidelity wins on overlap, `xliff` > `arb` > `po`); the
**source-language** catalog is never loaded as an override (the in-code default wins); untranslated/empty
entries are skipped; and a malformed file or catalog is skipped without crashing the app.

## Publishing

For a clean deployment, **merge the per-library files into one bundle per culture** at publish time:

```bash
dotnet apl merge --input <dir> --output <dir> --format arb
```

This runs automatically on `dotnet publish` (`ArchPillarLocalizationMergeOnPublish`, default on; set it to
`false` to keep the many-files layout). The merge **reuses the runtime's own load** (it *is* the loaded
data, dumped to one `Catalog` per culture), so a merged bundle resolves identically to the many-files path.

### Trimming, single-file, and AOT

The **files path is safe under every publish mode** — prefer it for mobile / WASM / trimmed / AOT apps
(ideally one merged bundle per culture). For the opt-in **embedded** path, validated on .NET 10 / linux-x64
(see TODO H1, reproduced by `samples/Localization/Localization.TrimSample`):

| Publish mode | Main-assembly embed (`[LocalizationCatalog]`) | Culture satellite |
|---|---|---|
| Full trim (`PublishTrimmed`, `TrimMode=full`) | ✅ works, no trimmer roots | ✅ preserved |
| Single-file + full trim | ✅ works | ✅ bundled into the exe |
| NativeAOT (`PublishAot`) | ✅ works | ⚠️ **does not load** — degrades to the in-code default |

NativeAOT cannot load a separate managed satellite assembly, so under AOT the satellite lookup fails *safely*
(it falls back to the in-code default, never crashes). **For AOT, prefer files (a merged bundle per culture)
or a main-assembly embedded catalog; avoid satellite embedding.** The embedded publishes are otherwise
IL-warning-clean (the resource/satellite/attribute reflection is not flagged by the trimmer).

`samples/Localization/Localization.AotSample` is a NativeAOT app built the recommended way — it localizes
through both AOT-safe paths (a loose file and a main-assembly embedded catalog, no satellite) and is verified
to resolve German after `dotnet publish -r <rid>`. (`Localization.TrimSample` is the broader spike that also
exercises the satellite path to demonstrate the AOT limitation above.)

The usual globalization caveat applies: an app published with `InvariantGlobalization=true` cannot select a
non-default culture, so it can never load non-default translations — standard .NET advice, not specific here.

## Options

| Option | Default | Purpose |
|---|---|---|
| `TranslationsDirectory` | `Translations` beside the binary | where catalogs are loaded from |
| `SourceCulture` | `en` | the source language, excluded from overrides |
| `Cultures` | `null` (discover) | restrict the loaded cultures |
| `FormatPrecedence` | `["xliff","arb","po"]` | winner on cross-format overlap |
| `MissingArguments` | `PassThrough` | render `{name}` unchanged vs. throw |
| `EnableHotReload` | `false` | watch the directory and reload (debounced) |

On the isolated `Localizer`, `Reload()` rebuilds the snapshot and swaps it in atomically; concurrent
`Translate` calls never see a torn state. The localizer is thread-safe, `IDisposable`, and designed to be a
singleton. The ambient store rebuilds and swaps the same way whenever a source is added.

## Performance

Lookup is built for the UI hot path. A **static label** (a literal message with no arguments) resolves
with **zero allocations** — the cached literal text is returned directly. A message with arguments
allocates only the result string (a thread-local `StringBuilder` is reused, and argument lookup avoids
building a dictionary). The zero-allocation guarantee is covered by allocation tests, and
`benchmarks/Localization.Benchmarks` measures the paths (`dotnet run -c Release --project
benchmarks/Localization.Benchmarks`).
