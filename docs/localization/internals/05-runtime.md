# 05 — Runtime

> Assembly: `ArchPillar.Localization` (the package applications reference). References `Formats.*` via the provider registry (spec 03) and `MessageFormat` (spec 04). No third-party runtime dependencies. Optional Dependency Injection / `IStringLocalizer` adapter is a separate package, deferred (Decision D-5, spec 00).

## Purpose

Render translatable call sites at runtime: look up the loaded override for the requested culture and key, fall back deterministically when absent, and format with the spec-04 engine. Loading is pluggable and hot-reloadable; lookup is lock-free and allocation-conscious.

## The call Application Programming Interface

The attributed surface from spec 01, on a `DefaultLocalizer`. The engine is a pure resolver: it takes a
snapshot source — a `CatalogStore` (directory-backed, optionally watched) or a fixed set of catalogs —
and resolves against it. Loading and the file watcher belong to `CatalogStore`, not the engine.

```csharp
public sealed class CatalogStore : IDisposable   // owns the directory load + watcher; exposes the snapshot
{
    public CatalogStore(LocalizerOptions options);
    public void Reload();
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
3. The **in-code `defaultMessage`** supplied at the call site (the terminal fallback — Decision D-1 = "code", spec 00).

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

- **Load whatever is available, mixed formats allowed (Decision D-14).** At startup (and on reload), enumerate every translation file in `TranslationsDirectory` (or the embedded manifest), detect each file's format by extension, parse it via the matching provider (spec 03) into a `Catalog`, and group by culture. Formats may be freely mixed in one directory — a realistic case is a project authored in ARB receiving an XLIFF back from a vendor for one language.
- **Precedence on overlap (prefer the more faithful format).** When a culture is present in more than one file, merge per key; on a per-key conflict, the entry from the higher-precedence format wins, per `FormatPrecedence`. The default orders by *runtime-value fidelity*: XLIFF and ARB store ICU verbatim (lossless), Portable Object's gettext-native plural cannot represent select/gender or nested constructs (lossy), so it ranks last. XLIFF and ARB are equally faithful for the runtime value, so their relative order matters only when both carry the same culture with conflicting text; the order is configurable.
- Only `TranslatedMessage` values are loaded into the override map; `NeedsTranslation`/empty translations are skipped so they fall through to the default (or a parent culture that does have them).
- **Authored vs runtime location (spec 06).** Catalogs are *authored* under the project's `ArchPillarLocalizationOutputPath`. They reach the runtime one of two ways, wired by the package `.targets`: copied beside the application binary (`CopyToOutputDirectory`, the default — `TranslationsDirectory` resolves to that copied subdirectory under `AppContext.BaseDirectory`), or embedded as assembly resources for single-file publish (`EmbeddedSource` set; the loader reads the manifest). The default works with no configuration; a consumer may override `TranslationsDirectory`.
- The source-language file is **not** loaded as overrides, whatever its format; the in-code default is the source-language truth (Decision D-1). Identify it by culture (its target culture equals `SourceCulture`) or by being a neutral template (a `.pot`, or an XLIFF with no distinct `trgLang`), and skip it.
- Culture discovery: map file name to culture (`messages.de.po`, `de.arb`, or a `de/` subdirectory — pick and document one layout, consistent with the tool's authored layout in spec 06; a per-culture subdirectory mirrors .NET satellite-assembly intuition). The set of available languages is simply whatever target files are present — added on demand by the tool or a translator (Decision D-12), never declared, so the runtime never assumes a fixed language set and a newly dropped-in file is picked up on next load (or hot reload). Hot reload via embedded resources is not possible, so `EnableHotReload` is ignored when `EmbeddedSource` is set.

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
- [ ] The source-language file is excluded from overrides regardless of its format (`.pot`, source `.arb`, or source `.xliff`).
- [ ] A missing runtime argument renders the placeholder unchanged under the default policy and does not throw.
