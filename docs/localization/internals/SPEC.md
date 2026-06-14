# ArchPillar.Extensions.Localization — Specification

The design contract for the localization library: what it guarantees, the concepts it is built
from, and the lines it deliberately will not cross. This is the developer-facing anchor; the
detailed design lives in the numbered specs (`00`–`06`) and the binding decisions in
[`DECISIONS.md`](DECISIONS.md), which **overrides the numbered specs wherever they differ**.

## Overview

A user-interface translation library. Translatable strings are written once, at the call site, as
an **in-code default** in ICU MessageFormat. A Roslyn source generator extracts those call sites at
compile time into a source-language template; translators fill in target-language catalogs (ARB,
XLIFF 2.1, or Portable Object); at runtime the catalogs load as **pluggable overrides** over the
in-code default. The default is always the source of truth and the terminal fallback, so an app
with no translation files still runs correctly.

## Goals

- **The in-code default is the source of truth and the terminal fallback** (D-1). A missing
  catalog, culture, or key never fails — it renders the default.
- **Extraction is attribute-driven and happens only in the compiler** (D-3, D-A). A parameter
  marked `[Translatable]` / `[TranslationDefault]` defines a translation site; the incremental
  generator is the one extraction engine.
- **Stable symbolic keys** (D-2) with a strongly-typed key registry (D-4).
- **One grammar — ICU MessageFormat** (D-6) — for arguments, `plural`/`selectordinal`/`select`, with
  embedded CLDR plural data (D-7).
- **Namespacing follows the `ILogger<T>` model** (D-H): every key is implicitly scoped by a
  *category* equal to the full type name of `T` in `ILocalizer<T>`. Users never manage namespaces.
- **Translations load through one ambient, layered store** (D-I), modeled on `IConfiguration`:
  process-wide, reachable with no services, built from layered sources where the last source wins —
  so a string localizes even from an exception thrown before any container exists. DI is an escape
  hatch over the same store, not a parallel system.
- **Files by default, embed by opt-in** (D-I): loose catalogs beside the binary are the trim/AOT-safe
  default; embedding routes catalogs into standard culture satellite assemblies.
- **The runtime lookup is allocation-lean**: a static literal resolves with zero allocations.
- **Interop and a migration on-ramp** (D-5, D-J): an `IStringLocalizer` adapter that *composes* over
  an existing factory, on-by-default extraction of `IStringLocalizer` indexer literals, and a no-op
  `L(...)` marker for strings that never reach a localizer.
- **No external dependencies** beyond the BCL; the container-format providers ship in the runtime.

## Non-Goals

- **No convention-based auto-mapping or name matching.** A translatable site is one that is
  explicitly attributed; nothing is guessed from a name.
- **No implicit work.** Nothing fetches translations, mutates a translator's files, or reconciles
  catalogs as a side effect — every such action is explicit and human-driven (the generator only
  emits the source template).
- **No runtime reflection except discovery.** Reflection is limited to one-time assembly/catalog
  discovery; the hot path is reflection-free.
- **No attributes for mapping configuration, no global registries.** The ambient store is the one
  deliberately-accepted piece of global state (D-I); there is no other.
- **`IStringLocalizer` is interop, not a source.** It resolves at runtime through the adapter but is
  never an extraction source — its keys carry no in-code default to harvest.
- **Nothing before .NET 8.** No .NET Framework, no `netstandard` for consumers (the Roslyn host
  components are `netstandard2.0` only because the compiler requires it).

## Conceptual model

```text
  call site                compile time              translators            runtime
  ─────────                ────────────              ───────────            ───────
  Translate(key, default)  generator extracts   →    target catalogs   →    ambient store
  [Translatable]           → source template         (ARB/XLIFF/PO)         (layered overrides)
  in-code ICU default                                                       └─ miss → in-code default
```

- **Detection** is the single definition of "what is translatable", shared by the analyzer, the
  generator, and the tool, so all three agree byte-for-byte (`01`, `02`).
- **Categories** scope keys by `typeof(T)` for `ILocalizer<T>`; the global category holds
  uncategorized keys.
- **The ambient store** layers sources **embedded < satellite < directory < host**, last-wins,
  rebuilds an immutable snapshot, and swaps it atomically; lookups are lock-free.
- **Formats** (`03`) round-trip catalogs; the runtime loads all three and prefers by fidelity.

## API surface

| Type | Role |
|------|------|
| `ILocalizer` / `ILocalizer<[TranslationScope] T>` | The native lookup API; `<T>`'s full name is the category. |
| `ILocalizerFactory` | Creates category-scoped localizers (the `ILoggerFactory` shape). |
| `Localized<TSelf>` | Optional base class: member name → key via `[CallerMemberName]`. |
| `Localizer` (static) | The process-wide ambient facade: `Default`, `Ambient`, `For<T>()`, `Translate(...)`, `AddCatalog`, `AddSource`, `Configure(options)`, `Initialize(options, eager)`, `Reset()`. All config flows through `LocalizerOptions`. |
| `LocalizationContext` | The instantiable environment behind the facade (catalogs + config + localizers); the ambient context is one of these. Construct one directly for an isolated, static-free setup. |
| `DefaultLocalizer` | The pure resolution engine over a `CatalogStore` (or a fixed catalog set) for tests/multitenancy. |
| `CatalogStore` | Owns the layered catalogs, the directory watcher, and assembly discovery; produces the snapshot the engine resolves against. |
| `TranslatableAttribute` / `TranslationDefaultAttribute` / `TranslationContextAttribute` / `TranslationCommentAttribute` / `TranslationScopeAttribute` | Mark the parameters/type-parameters detection reads. |
| `TranslationMarkers.L` | No-op extraction marker for off-localizer strings. |
| `Catalog` / `CatalogEntry` / `ITranslationFormat` / `ITranslationSource` | The catalog model and extension points. |
| `IStringLocalizer*` adapters (DI package) | Interop over the ambient store, composing over a prior factory. |

Full signatures are in the numbered specs and the runtime spec ([`05-runtime.md`](05-runtime.md)).

## Error philosophy

- **Fail fast at build time.** Unmapped or conflicting keys, invalid ICU, a placeholder with no
  argument, a plural/select missing its `other` branch — these are diagnostics (`APL0001`–`APL0008`)
  at the call site, not query-time surprises. A non-constant key is an error.
- **Never fail at runtime.** A missing snapshot, culture, key, malformed file, or absent satellite
  degrades to the in-code default (or a parent culture) for that one call; discovery failures are
  swallowed. The terminal fallback always renders.

## What this library deliberately does not do

- It does not auto-discover translatable strings by name, type, or convention — only by attribute.
- It does not write, fetch, or reconcile target catalogs as a build side effect.
- It does not require DI, a host, or a file system to localize.
- It does not load satellite assemblies under NativeAOT (it degrades to the in-code default; prefer
  files or a main-assembly embedded catalog for AOT — see [`../recommendations.md`](../recommendations.md)).
- It does not extract `IStringLocalizer`/`.resx` keys, DataAnnotations messages, or view-localization
  calls (no in-code default to harvest); the adapter serves them at runtime only.
