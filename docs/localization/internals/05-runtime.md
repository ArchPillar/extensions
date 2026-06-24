# 05 — Runtime

> Assembly: `ArchPillar.Localization` (the package applications reference). References `Formats.*` via the provider registry (spec 03) and `MessageFormat` (spec 04). No third-party runtime dependencies. Optional Dependency Injection / `IStringLocalizer` adapter is a separate package, deferred (Decision D-5, spec 00).

## Purpose

Render translatable call sites at runtime: look up the loaded override for the requested culture and key, fall back deterministically when absent, and format with the spec-04 engine. Loading is pluggable and hot-reloadable; lookup is lock-free and allocation-conscious.

## The call Application Programming Interface

The attributed surface from spec 01, on a `DefaultLocalizer`. The engine is a pure resolver: it takes a
snapshot source — a `CatalogStore` (provider-backed, optionally watched) or a fixed set of catalogs —
and resolves against it. Loading and the file watcher belong to `CatalogStore`, not the engine.

```csharp
public sealed class CatalogStore : IDisposable   // owns an ordered list of catalog providers; exposes the snapshot
{
    public CatalogStore(LocalizerOptions options);   // auto-wires a directory provider (+ resource for the ambient store)
    public void Reload();
    public event Action? CatalogsChanged;            // raised after any commit that changed the snapshot
    // Provider registration and asynchronous loading live on LocalizationContext / Localizer:
    //   AddProvider(ICatalogProvider), LoadCultureAsync(culture), PreloadAllAsync()
}

public sealed class DefaultLocalizer : ILocalizer
{
    public DefaultLocalizer(CatalogStore store, LocalizerOptions? options = null);   // resolves store.Snapshot live
    public DefaultLocalizer(IEnumerable<Catalog> catalogs, LocalizerOptions? options = null);   // isolated, fixed
    public static DefaultLocalizer FromCatalogs(IEnumerable<Catalog> catalogs, LocalizerOptions? options = null);

    public string Translate(
        [Translatable] string key,
        [TranslationDefault] string defaultMessage,
        params (string Name, object? Value)[] arguments);

    public string Translate(
        [Translatable] string key,
        [TranslationDefault] string defaultMessage,
        [TranslationContext] string context,
        params (string Name, object? Value)[] arguments);

    // Culture is taken from CultureInfo.CurrentUICulture by default;
    // an explicit overload allows passing a CultureInfo for server scenarios.
    public string Translate(CultureInfo culture, string key, string defaultMessage,
        string? context, params (string Name, object? Value)[] arguments);
}
```

- The `params (string Name, object? Value)[]` form keeps argument names available both for ICU rendering (names are required by the grammar) and for the analyzer's `APL0003`/`APL0004`. A dictionary overload exists for callers who build arguments dynamically.
- Consumers may wrap these in ergonomic helpers; detection (spec 01) follows the wrapper. The library ships at least one terse helper (e.g., a static `T.Translate` / `T._`) but imposes no required base type, partial class, or per-key method.

## Lookup and the fallback chain

Resolution order for `(culture, key, context)`:

1. The override entry for the **exact requested culture** in the loaded snapshot.
2. Each **parent culture** in turn (`de-AT` → `de` → invariant), using `CultureInfo.Parent`.
3. The **in-code `defaultMessage`** supplied at the call site (the terminal fallback — Decision D-1, spec 00). A source-language *override* is not a separate step: it is an ordinary entry for the source culture resolved at step 1/2, sitting above this floor (Decision D-L).

The looked-up value (or the default) is an ICU MessageFormat string. It is parsed (cached) into a spec-04 `Message` and formatted against `culture` and the supplied arguments. Plural categories use `culture`, not the source language, so a German override pluralizes by German rules even though the key was authored in English.

Because the default is always present at the call site, **a missing snapshot, a missing culture, or a missing key never fails** — it degrades to the source language for that one call. This is the runtime expression of the spec-00 invariant.

## Snapshot model and lock-free reads

```csharp
internal sealed class TranslationSnapshot
{
    // culture (BCP-47, case-insensitive) -> (compositeKey -> ICU message string)
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ByCulture { get; init; }
    // cache of parsed messages, keyed by the message string, shared across cultures
    public ConcurrentDictionary<string, Message> ParsedCache { get; } = new();
}
```

- `compositeKey` combines `Key` and `Context` with the same convention the providers use (spec 03), so lookup matches storage exactly.
- The live snapshot is held in a single field published with `Volatile.Write` / read with `Volatile.Read` (or `Interlocked.Exchange` on swap). Readers never lock and never observe a half-built table.
- **Reload builds a brand-new `TranslationSnapshot` fully in memory, then swaps the reference in one operation.** In-flight reads continue against the old snapshot; subsequent reads see the new one. No reader-side synchronization, no torn state.
- The parsed-message cache lives on the snapshot so a reload naturally discards stale parses; within a snapshot it is populated lazily and is safe for concurrent readers.

## Loading

```csharp
public sealed class LocalizerOptions
{
    // Defaults to the catalog subdirectory copied beside the app by the package
    // .targets (spec 06); resolved relative to AppContext.BaseDirectory.
    public string TranslationsDirectory { get; init; } = DefaultCopiedDirectory();
    public string SourceCulture { get; init; } = "en";
    public IReadOnlyList<string>? Cultures { get; init; }   // null = discover from directory
    // Files of ANY supported format are loaded; this orders preference when the
    // same culture+key appears in more than one file. Higher index = lower priority.
    public IReadOnlyList<string> FormatPrecedence { get; init; } = ["xliff", "arb", "po"];
    public bool EnableHotReload { get; init; } = false;     // Decision D-9
    public TimeSpan HotReloadDebounce { get; init; } = TimeSpan.FromMilliseconds(250);
    public MissingArgumentPolicy MissingArguments { get; init; } = MissingArgumentPolicy.PassThrough;
    public IManifestResourceSource? EmbeddedSource { get; init; } // set when catalogs are embedded
}
```

- **Providers, with discovery split from load.** The store loads from an ordered list of `ICatalogProvider`s (lowest-precedence-first) and never knows where the bytes come from. A provider discovers *which* catalogs exist at construction — it is *born ready*, exposing a synchronous `CatalogDescriptor` inventory (`Catalogs` / `CatalogsFor(culture)`) — and each descriptor carries a `CatalogSource` opener (`Synchronous(Func<Stream>)` or `Asynchronous(Func<CancellationToken, ValueTask<Stream>>)`) that the store opens only when it loads that catalog. The store auto-wires a `DirectoryCatalogProvider` (and a `ResourceCatalogProvider` for the ambient store); a host adds others through `AddProvider`. The directory and resource providers are synchronous, so the store loads them inline; the manifest provider is asynchronous (see the Blazor bullet below). `CultureLoading` chooses between loading every enumerable culture at startup (`Eager`, the server default) and loading a culture's catalogs only on first use (`OnDemand`, the client default); the default is platform-derived.
- **Load whatever is available, mixed formats allowed (Decision D-14).** At startup (and on reload), enumerate every translation file a provider sees (e.g. the directory's `Catalogs`, or the embedded/satellite resources), detect each file's format by extension, parse the opened stream via the matching format (spec 03) into a `Catalog`, and group by culture. Formats may be freely mixed in one directory — a realistic case is a project authored in ARB receiving an XLIFF back from a vendor for one language.
- **Precedence on overlap (prefer the more faithful format).** When a culture is present in more than one file, merge per key; on a per-key conflict, the entry from the higher-precedence format wins, per `FormatPrecedence`. The default orders by *runtime-value fidelity*: XLIFF and ARB store ICU verbatim (lossless), Portable Object's gettext-native plural cannot represent select/gender or nested constructs (lossy), so it ranks last. XLIFF and ARB are equally faithful for the runtime value, so their relative order matters only when both carry the same culture with conflicting text; the order is configurable.
- Only `TranslatedMessage` values are loaded into the override map; `NeedsTranslation`/empty translations are skipped so they fall through to the default (or a parent culture that does have them).
- **Authored vs runtime location (spec 06).** Catalogs are *authored* under the project's `ArchPillarLocalizationOutputPath`. They reach the runtime one of two ways, wired by the package `.targets`: copied beside the application binary (`CopyToOutputDirectory`, the default — `TranslationsDirectory` resolves to that copied subdirectory under `AppContext.BaseDirectory`), or embedded as assembly resources for single-file publish (`EmbeddedSource` set; the loader reads the manifest). The default works with no configuration; a consumer may override `TranslationsDirectory`.
- The source-language catalog **is** loaded, as an override layer like any other culture, *above* the in-code default (which stays the terminal fallback — Decision D-L amends D-1). It is not special-cased on load: the same per-entry filter that skips untranslated target entries also drops a source *echo* (an entry stored `NeedsTranslation` because it merely repeats the in-code default), so only a genuine source override — wording a human edited away from the default — is loaded. A render with no source catalog, or a source key with only an echo, falls through to the in-code default unchanged. The language-neutral template (a `.pot`, or an XLIFF with no distinct `trgLang`) carries no culture and so contributes nothing.
- Culture discovery: map file name to culture (`messages.de.po`, `de.arb`, or a `de/` subdirectory — pick and document one layout, consistent with the tool's authored layout in spec 06; a per-culture subdirectory mirrors .NET satellite-assembly intuition). The set of available languages is simply whatever target files are present — added on demand by the tool or a translator (Decision D-12), never declared, so the runtime never assumes a fixed language set and a newly dropped-in file is picked up on next load (or hot reload). Hot reload via embedded resources is not possible, so `EnableHotReload` is ignored when `EmbeddedSource` is set.
- **No file system: load over HTTP (Blazor WebAssembly).** A browser has no readable file system, so the directory provider finds nothing; the `ManifestCatalogProvider` is its client-side counterpart. It is the only **asynchronous** provider: `await ManifestCatalogProvider.CreateAsync(httpClient, manifestUri)` fetches the build-emitted manifest (spec 06) up front and builds its descriptor set, each descriptor carrying a `CatalogSource.Asynchronous` opener that fetches that one catalog over `HttpClient` when the store loads it. A missing, unrecognised, or malformed asset is skipped so the app degrades to its in-code defaults. Because the fetch is genuinely asynchronous, the manifest's catalogs are never opened on the synchronous lookup path: the host registers the provider with `Localizer.AddProvider` and then awaits `LoadCultureAsync(culture)` (no flash) — a synchronous miss instead returns the default now and queues a coalesced background load that raises `CatalogsChanged` when it lands. The `…WebAssembly` package's `UseArchPillarLocalizationAsync` host helper performs that create-add-load for the common case. The manifest is what directory enumeration is on disk — over HTTP there is no directory to list — and the build registers it (and the catalogs) as static web assets through the Razor pipeline (spec 06), so it is served, fingerprinted, and compressed in both the development build and the published output. The manifest provider lives in the core runtime (`HttpClient` is a BCL type, so it stays dependency-free); serving the catalogs from an ASP.NET Core host is the `ArchPillar.Extensions.Localization.AspNetCore` package.

## Hot reload (opt-in)

- When `EnableHotReload` is true, attach a `FileSystemWatcher` to `TranslationsDirectory`.
- **Debounce:** editors and copy operations fire multiple events per save; collect events and rebuild only after a quiet period of `HotReloadDebounce`. Rebuild only the affected culture(s) where the layout makes that determinable, otherwise rebuild the whole snapshot; either way publish via the same atomic swap.
- The watcher is the only thing gated behind the flag. With it off, the runtime is complete and correct; turning it on adds one debounced handler that calls the identical reload path. The core lookup never references the watcher.
- Expose a manual `Reload()` for hosts that prefer to trigger reloads themselves (for example after pulling new translation files from a Translation Management System).

## Diagnostics and observability

- Optional `ILogger`-style hook (via a minimal delegate, not a hard `Microsoft.Extensions.Logging` dependency) to report: file parse failures on load (load the rest; a bad file must not take down the application), missing-argument occurrences (debug), and reload events.
- A parse failure during load logs and skips that file's entries (they fall through to default/parent), preserving the never-crash invariant.

## Threading and lifetime

- `DefaultLocalizer` is thread-safe for concurrent `Translate` calls; a `CatalogStore` reload swaps its snapshot atomically, so a resolving localizer observes it on the next lookup without tearing.
- It owns the `FileSystemWatcher` (when enabled) and is `IDisposable`.
- Designed to be a singleton; multiple instances are permitted (e.g., different directories) and independent.

## Acceptance criteria

- [ ] With no translation files present, every `Translate` call returns the formatted in-code default for the current culture's plural rules.
- [ ] An override for `de` is used for `de-AT` via parent walk; an override for `de-AT` takes precedence over `de`.
- [ ] A key present in `de` but absent in `fr` renders the default for `fr` while still rendering the `de` override for `de` — graceful per-key, per-culture degradation.
- [ ] An untranslated (empty/`NeedsTranslation`) entry in a loaded file falls through to default/parent rather than rendering empty.
- [ ] Concurrent `Translate` calls during a `Reload()` never throw, never deadlock, and always return either the pre- or post-reload value (no torn reads); verified under a stress test.
- [ ] A malformed translation file is skipped on load with a logged error; the rest load; the application runs.
- [ ] With hot reload enabled, editing a translation file is reflected after the debounce window without restart; with it disabled, no `FileSystemWatcher` is created.
- [ ] Plural rendering uses the target culture (a German override pluralizes by German rules); parsed messages are cached and not re-parsed per call.
- [ ] A directory containing a mix of `.arb`, `.xliff`, and `.po` files loads them all; each language resolves from whatever file(s) carry it.
- [ ] When the same culture appears in both a `.po` and a `.arb`/`.xliff` with conflicting text for a key, the ICU-native file wins under the default `FormatPrecedence`; reordering `FormatPrecedence` changes the winner.
- [ ] A genuine source-language override (an `.arb`/`.xliff` entry whose source wording was edited away from the in-code default) is loaded above the in-code default and wins for the source culture; a source *echo* of the default, and a language-neutral `.pot` template, contribute nothing.
- [ ] A missing runtime argument renders the placeholder unchanged under the default policy and does not throw.
