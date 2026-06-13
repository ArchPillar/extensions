# Localization — Binding Decisions & Phase Plan

This document records the decisions made for the ArchPillar implementation of the
localization library. **It overrides the numbered specs (`00`–`06`) wherever they
conflict.** The numbered specs remain the detailed design reference; this file is the
source of truth for what we actually build.

## Naming

- Library family root namespace and package prefix: **`ArchPillar.Extensions.Localization`**.
- The original specs use the working codename `ArchPillar.Localization`; read every such
  reference as `ArchPillar.Extensions.Localization`.
- Diagnostic identifier prefix stays **`APL`** ("ArchPillar Localization").

| Spec name | Repository name |
|---|---|
| `ArchPillar.Localization.MessageFormat` | `ArchPillar.Extensions.Localization.MessageFormat` |
| `ArchPillar.Localization.Abstractions` | `ArchPillar.Extensions.Localization.Abstractions` |
| `ArchPillar.Localization.Detection` | folded into `ArchPillar.Extensions.Localization.Analyzers` |
| `ArchPillar.Localization.Analyzers` | `ArchPillar.Extensions.Localization.Analyzers` (also contains detection + the generator) |
| `ArchPillar.Localization.Generator` | folded into `ArchPillar.Extensions.Localization.Analyzers` |
| `ArchPillar.Localization.Reconcile` | `ArchPillar.Extensions.Localization.Reconcile` |
| `ArchPillar.Localization.Tooling` | `ArchPillar.Extensions.Localization.Tooling` |
| `ArchPillar.Localization.Formats.*` | `ArchPillar.Extensions.Localization.Formats.*` |
| `ArchPillar.Localization` (runtime) | `ArchPillar.Extensions.Localization` |

## Core principle: no implicit work

The maintainer's standing principle. Nothing that mutates a translator's files, fetches
translations, or reconciles catalogs happens as a side effect. Every such operation is an
explicit, human-driven (or explicitly-scripted) action. This principle is why the generator
never reads or writes a target file and why all reconciliation lives behind a tool that
someone runs on purpose.

## Decisions that override the specs

### D-A — Extraction happens only in the compiler.
The `IIncrementalGenerator` is the **only** extraction engine. It holds the `Compilation`
and runs detection in-process during a normal build — no re-analysis of the codebase ever
happens out of band. **The `dotnet` tool's `template` command (which re-extracted via
`MSBuildWorkspace`) is removed.** Spec 02's "tool can also produce the template out of
process" path does not ship.

### D-B — The generator writes only on a real build.
Design-time / IDE builds never write to disk. The generator still *computes* in the IDE
(for live diagnostics and the typed key registry), but the template is written only on an
actual build, exactly like the C# documentation XML file. It reads `DesignTimeBuild` (via
`CompilerVisibleProperty`) to suppress the design-time write. `LiveExtraction` is retained
as an opt-in property but defaults to **off**. Writes are governed by write-if-changed and
are atomic (temp-file-then-move).

### D-C — Reconciliation is the tool's job (spec as written).
The generator emits **only the source-language template**. It never creates, reads, or
touches a target-language file. All target-file operations — `add <lang>`, `sync`,
`convert` — live in the `dotnet` tool and run on demand. The tool operates purely on the
`Catalog` model (the on-disk template plus existing target files); it never re-parses code.
The CI gate is **`sync --check`** (compares existing target files against the on-disk
template, writes nothing, exits non-zero on drift).

### D-D — The library never forces a catalog location.
Target catalogs can live wherever the team wants — beside the source, in a separate
repository, in a translation store, anywhere. The library and tool impose no location and
discover nothing implicitly:

- The tool takes **explicit paths** (template location, target directory). It never assumes
  targets sit in any particular place, and there is no discovery magic.
- `OutputPath` in the source tree is where the generator writes **the template** (a build
  output, git-ignorable like the doc XML). Target files may or may not share that directory —
  that is the team's choice, not the library's.
- The package `.targets` copy-to-output / embed step (spec 06) applies to whatever target
  files are *present in the configured directory at build time*; placing them there is an
  explicit step. The build never fetches, never reconciles, never assumes a language set.
- Anyone who wants reconciliation (in CI/CD or locally) wires an explicit `sync` /
  `sync --check` step.

### D-F — Dependency-free providers bundle into the core runtime.
The container-format providers (ARB, Portable Object, XLIFF) have no dependencies beyond the BCL
on the runtime's target frameworks (`System.Text.Json` and `System.Xml` are in-box on net8/9/10).
They therefore ship **inside the core runtime assembly** (`ArchPillar.Extensions.Localization`),
not as separate `Formats.*` packages — the family does not split dependency-free code into tiny
DLLs. The runtime registers all bundled providers by default.

`MessageFormat` and `Abstractions` remain separate assemblies only because the analyzer and the
generator load them inside the compiler (`netstandard2.0`); the providers are runtime-only and so
have no such constraint. The compile-time generator emits the source template with a small internal
writer (spec 02 / Phase 7), so it needs no provider assembly and adds no `System.Text.Json`
dependency to the Roslyn host.

### D-G — APL0009 (live stale-source diagnostic) is not implemented; staleness is the tool's job.
The analyzer ships APL0001–APL0008. APL0009 (flagging, live in the editor, entries whose stored
fingerprint differs from the current default) is intentionally **not** implemented: it would require
the `netstandard2.0` analyzer to parse translation catalogs (the ARB/XLIFF/PO parsers are in the net
runtime), the catalogs are often not present in the project (D-D — they may live in a separate repo),
and it duplicates `dotnet apl sync --check`, which detects drift canonically. Staleness is therefore
covered by the tool, not the editor.

### D-E — Defaults for the specs' open questions.
- **Catalog layout:** one file per locale, flat in `OutputPath`. ARB `en.arb` / `de.arb`;
  XLIFF `en.xliff` / `de.xliff`; Portable Object `messages.pot` template + `messages.de.po`.
- **Key-naming policy:** free-form by default; the optional `APL0008` regex check is off
  unless `ArchPillarLocalizationKeyPattern` is configured.
- **Typed accessors:** v1 ships the `const` key registry only (D-4). Typed accessor methods
  are deferred.
- **Authoring format:** a single format per project, defaulting to `arb`. The runtime still
  loads mixed formats and prefers by fidelity (D-14).
- **Composite key convention:** `Key` and `Context` combine as `context + "\u0004" + key`
  (the gettext `EOT` convention), defined once in `Abstractions` and shared by every provider
  and the runtime lookup.

### D-H — Namespacing follows the `ILogger<T>` model; users never manage namespaces.
Keys live in one global lookup, but every key is implicitly scoped by a **category** equal to the
full type name of the `T` in `ILocalizer<T>` — exactly as `ILogger<T>` derives its category. There is
no namespace configuration: no MSBuild property, no attribute the user sets, nothing to manage. Shared
strings are shared the ordinary way — a shared class/service injected as `ILocalizer<SharedResource>` —
which doubles as honest code deduplication. This refines D-5 and supersedes the early flat global keyspace:

- **The native API is `ILocalizer` / `ILocalizer<T>` / `ILocalizerFactory`**, modeled on
  `ILogger`/`ILoggerFactory`. `ILocalizer<T>` derives its category from `typeof(T).FullName`;
  `ILocalizerFactory.Create(string category)` is the dynamic escape hatch (== `CreateLogger(string)`).
- **The in-code default stays** (D-1): the native method is `Translate(key, default, args)`, not the
  default-less `IStringLocalizer` indexer. `ILocalizer<T>` is therefore *richer* than
  `IStringLocalizer<T>`; the BCL `IStringLocalizer` adapter (D-5) remains, for interop only.
- **The runtime snapshot gains a category tier**: `culture → category → key → message`. An
  `ILocalizer<T>` resolves its category's sub-map once (cached per `T`), so a literal lookup stays
  zero-allocation (no per-call key concatenation) — the performance guarantee is preserved.
- **Extraction derives the category from the receiver's `T`, attribute-driven** (D-3): the generic
  parameter carries `[TranslationScope]`, so detection reads the type argument's full name without
  hardcoding any type or method name, and anyone can roll their own scoped localizer. The compile-time
  category therefore matches the runtime category exactly.
- `DefaultLocalizer` (concrete) becomes an `ILocalizer` implementation; the sealed implementations sit behind
  the interfaces.

### D-I — Translations load through one ambient, layered store (the configuration model); DI is an escape hatch.
A translated string must render where no services exist — most importantly an **exception message** thrown
from a static context, deep in a library, before any container is built. Anything that needs DI to localize
is broken for that case. So the translation/override data is **ambient global state** — accepted
deliberately despite the general aversion to it — modeled on `IConfiguration`: one process-wide store built
from **layered sources where the last source wins**.

- **Uniform layered sources.** A library's embedded translations and a host's overrides are not different
  kinds of thing — both are sources layered into the one store. Override = "add a later source." A lookup is
  one priority-ordered, lock-free read; a miss falls to the in-code default (the floor, supplied at the call
  site, never stored). The category (D-H) is how sources are keyed.
- **The lookup/merge code lives in archpillar.** A library references archpillar and calls in; nothing is
  duplicated per library. `ILocalizer<T>`, the generated per-assembly localizer, and the bare `DefaultLocalizer`
  are category-scoped read views over the one store.
- **Lazy, assembly-load-driven.** Assemblies load on demand, so sources are discovered by reacting to loads,
  not by an up-front scan: read embedded translations from already-loaded assemblies at init, and subscribe
  to `AppDomain.CurrentDomain.AssemblyLoad` to pull in each assembly's translations as it loads. This is
  self-correcting — a library's `Translate` cannot run before its assembly is loaded, and a host override for
  a not-yet-loaded library waits in the store and wins when that library appears. Each assembly advertises
  its embedded catalogs with an assembly attribute, so discovery is an attribute read, not a resource scan.
- **Immutable snapshot, atomic swap.** Adding a source rebuilds the merged snapshot and swaps it atomically —
  the existing lock-free machinery — so lookups stay zero-allocation.
- **DI is the escape hatch.** `AddArchPillarLocalization` feeds the same ambient store and offers injectable
  views; it is a convenience over the ambient, never a parallel system or a requirement. An explicitly
  constructed isolated `DefaultLocalizer` remains available for tests and multi-tenant scenarios, and a test reset
  keeps the suite deterministic against the shared store.
- **Targeting: `net8.0;net9.0;net10.0` — nothing before .NET 8.** The maintainer supports no .NET Framework
  and no `netstandard` for consumers. The runtime therefore stays `net8.0;net9.0;net10.0`; a library that
  localizes references it directly and is itself net8+. `System.Text.Json` and `System.Xml` are in-box on
  every supported target, so there is **no package dependency and no `#if`/polyfill work**. (The
  analyzer/generator, `MessageFormat`, and `Abstractions` remain `netstandard2.0` only because the Roslyn
  host requires it — the compiler's constraint, not a consumer-reach choice.)
- **Embedded catalogs lean into satellite assemblies.** When a library *opts into* embedding (the default
  stays files), catalogs are named `<name>.<culture>.<ext>` and MSBuild routes them to standard per-culture
  **satellite assemblies** — deliberately working *with* .NET's resource convention and people's existing
  knowledge, not against it. The ambient store loads them the framework way: lazily, per requested culture,
  via `Assembly.GetSatelliteAssembly(culture)`, walking parent cultures. The culture-neutral attribute path
  (main-assembly resource) remains for source-language or merged catalogs that have no single culture.
  Trimming / single-file / AOT compatibility is validated by spike before it is relied on.

This refines D-H (the namespacing model stands) and revises D-5/D-B/D-F where they assumed DI-first or
per-instance loading.

### D-J — Migration on-ramp: scan `IStringLocalizer` sites + a no-op `L(...)` marker; the adapter composes, never replaces.
A team adopting this library from an existing `IStringLocalizer` / `.resx` codebase must not face a rewrite
to get value, or the migration cost alone makes them discard it. Three deliberate concessions, grounded in
how `IStringLocalizer` actually works (verified against the docs):

- **Nothing of the framework's own is at risk.** No .NET runtime/BCL message, Identity error, or MVC
  model-binding message flows through `IStringLocalizer` — those use `ResourceManager` and dedicated
  providers directly. The only strings on the `IStringLocalizer` path are **opt-in app code**
  (`AddDataAnnotationsLocalization`, view localization, direct injection). So the thing we must not break is
  **the consumer's own `.resx`**, served by the default `ResourceManagerStringLocalizerFactory`.
- **The adapter composes.** Our `IStringLocalizerFactory`/`IStringLocalizer` tries the ambient store first
  and, on a miss, **falls through to the previously-registered factory** (using `LocalizedString.ResourceNotFound`
  for found-awareness — reversing the earlier "value-or-name only" stance, which would have shadowed existing
  `.resx`). Existing translations keep working alongside ours; adoption is additive.
- **`IStringLocalizer` call sites are extracted, on by default.** The detector recognizes the indexer
  (`this[name]` / `this[name, params object[]]`) by symbol, not attribute. **The indexed literal is both the
  key and the default** (matching BCL semantics, where a not-found lookup returns the name). Category is
  `typeof(T)` for `IStringLocalizer<T>`, global for the non-generic indexer. Positional `{0}` placeholders
  are captured verbatim (no rewrite — already valid). This is automatic so a migrating codebase's strings
  appear in the catalog with zero annotation work.
- **`L(...)` — a no-op marker for everything else.** Strings that never touch a localizer (log messages,
  `throw new(...)` text) get a terse passthrough: `using static …` then `L("Email is required")`. The method
  returns its argument unchanged at runtime (no setup, no ambient dependency); its only job is to carry
  `[Translatable]` + `[TranslationDefault]` so the existing detector harvests the literal (key = default =
  the text; null receiver → global category). It piggybacks entirely on the current attribute-driven
  detection — no special-casing.

- **The interop ships as its own package, separable by design.** The `IStringLocalizer` adapters live in
  `ArchPillar.Extensions.Localization.StringLocalizer` (registered via `AddArchPillarStringLocalizer`), apart
  from the native DI registration in `ArchPillar.Extensions.Localization.DependencyInjection`
  (`AddArchPillarLocalization`). The interop package depends on the DI package (and on the full
  `Microsoft.Extensions.Localization`, for the `ResourceManager`/`.resx` factory it composes over and
  registers itself — Decision D-F3); nothing native or core depends back on it, so the registrations are
  purely additive. A consumer drops the package and its one `AddArchPillarStringLocalizer` call once their code
  no longer touches `IStringLocalizer`. The dependency direction (interop → DI → core) keeps the native path
  dependency-light: a DI-only consumer never drags in the `ResourceManager` machinery. Our composing
  `IStringLocalizerFactory` is the single seam every opt-in framework feature flows through — MVC's
  `IViewLocalizer`/`IHtmlLocalizer` and `AddDataAnnotationsLocalization` all resolve through
  `IStringLocalizerFactory.Create(...)` — so the one package covers them without per-feature adapters, and
  ASP.NET Core/Blazor need nothing more (the framework itself never consumes `IStringLocalizer`; request
  culture flows through `CultureInfo.CurrentUICulture`, which the native path already reads).

This refines D-5 (interop is no longer a one-way value-or-name adapter — it composes) and extends D-3
(attribute-driven detection still covers the marker; the `IStringLocalizer` indexer is the one symbol-based
exception, justified by it being a fixed BCL shape we cannot annotate).

### D-K — Extraction reads the built assembly (IL), not source — so it covers Razor/Blazor/MVC.
A source-generator-based extractor has a structural blind spot: a Roslyn source generator only sees the
original user source, never another generator's output. Razor/`.cshtml` is compiled to C# by the *Razor*
source generator, so every string in markup (`@Localizer.Translate("home.title", "Inbox")`,
`Strings["inbox.summary", 3]`) is invisible to our generator. This is not Blazor-specific: it is **any string
that exists only in another source generator's output**. (Hand-written C#, code-behind `.razor.cs`, and T4
output are all normal source and extract fine; the boundary is "who wrote the syntax — you, or another
generator".)

Dead ends, ruled out:
- **Move extraction to the analyzer.** Analyzers *can* see generated code (`GeneratedCodeAnalysisFlags.Analyze`),
  but they cannot emit anything except diagnostics, and `File`/`Console`/etc. are banned in analyzer code by
  **RS1035** (which our projects enable via `EnforceExtendedAnalyzerRules`). Exporting the catalog through the
  diagnostic/SARIF channel is an abuse of that channel. Analyzers stay pure observers (they *may* validate
  Razor strings, but never produce the catalog).
- **Parse `.razor`/`.cshtml` ourselves.** That is reimplementing the Razor compiler — fragile, perpetually
  behind, and a non-goal.

**Decision:** extraction is done by the `dotnet apl` **tool**, reading the **built assembly's IL** (method
bodies, via `System.Reflection.Metadata`). The assembly is the one artifact that contains *everything* —
every generator's output compiled in — so this is complete by construction, with no blind spot downstream.
The tool is a normal console app, so the I/O the analyzer is forbidden is exactly what the tool may do. Proven
by a probe against the Blazor sample: the IL scan recovered the Razor call sites, defaults (a full ICU plural
among them), and arguments that the generator never saw.

**Consequences:**
- The baked `[GeneratedLocalizationTemplate]` attribute is **superseded and removed** — it was only an
  intermediate the tool read; nothing at runtime ever used it. The generator keeps only the typed key registry
  (IDE autocomplete, C# keys). The runtime is unchanged: in-code defaults remain the source language and the
  terminal fallback (D-1), and translation *delivery* (loose files or embedded satellites) is untouched.
- Extraction stays a **post-build** step (the `AfterBuild` MSBuild target already is one), so there is no
  IDE/keystroke cost. Scanning one assembly's own method bodies is tens of milliseconds — dwarfed by tool
  startup and the build; assemblies that do not reference the localizer types are skipped via the
  `TypeRef`/`MemberRef` tables.
- Implementation surface: (1) stack-aware association of constant operands to each call; (2) the category from
  the `ILocalizer<T>`/`IStringLocalizer<T>` generic instantiation in metadata; (3) source locations from the
  PDB (Razor's `#line` maps sequence points back to the `.razor`); (4) identify translate methods by their
  `[Translatable]`/`[TranslationDefault]` parameter attributes, so any wrapper works, not hard-coded names.

This is a sizeable engine and is tracked as its own work item; it does not change the `extract` command's
surface or output, only how it reads.

## What is unchanged from the specs

All other decisions in `00-architecture.md` (D-1 … D-14) stand: the in-code default is the
runtime source of truth and terminal fallback (D-1); stable symbolic keys (D-2);
attribute-driven detection (D-3); the typed key registry (D-4); ICU MessageFormat as the one
grammar (D-6); embedded CLDR plural data (D-7); the three shipped formats with ARB as default
(D-8); opt-in hot reload (D-9); delete-on-removal of obsolete keys (D-11); on-demand languages
(D-12); `convert` as a tool capability (D-13); runtime loads-all-formats-prefers-by-fidelity
(D-14). `IStringLocalizer` / DI interop stays deferred to an integration phase (D-5).

## Performance (cross-cutting)

The runtime lookup is on the UI hot path, so it must be lightning fast and allocation-lean:

- **A static label resolves with zero allocations.** A literal message (no arguments) returns its
  cached text directly; argument lookup is a span/array scan, not a dictionary; and a thread-local
  `StringBuilder` is reused so a dynamic render allocates only the result string (inherent).
- This is **pinned by allocation tests** (`GC.GetAllocatedBytesForCurrentThread`, asserting exact zero
  on the literal path) and tracked by a **BenchmarkDotNet** project (`benchmarks/Localization.Benchmarks`).
- Benchmarks + allocation tests accompany the runtime (Phase 4) and any later change to the hot path.

## Phase plan

Every phase follows the repository's conventions: **TDD** (tests first), **zero-warnings**
build, and ships with **tests + docs + a sample where meaningful**. Target frameworks follow
each assembly's role (`netstandard2.0` for anything the Roslyn host loads; `net8.0;net9.0;net10.0`
for runtime assemblies).

| # | Phase | Delivers |
|---|---|---|
| 1 | `MessageFormat` | ICU parser / AST / validator / formatter; CLDR plural codegen (`PluralRules`, operands, `GettextOrder`); placeholder extraction |
| 2 | `Abstractions` | attributes; `Catalog` / `CatalogEntry`; provider interface + capability flags; composite-key convention; format registry |
| 3 | `Formats.Arb` | default ARB provider; byte-stable round-trip; state via `x-state`; fingerprint persistence |
| 4 | **Runtime** `Localizer` | `DefaultLocalizer`, fallback chain, lock-free snapshot, loading + precedence, hot reload, static helper — first usable slice (hand-authored ARB) + sample |
| 5 | `Formats.Po` + `Formats.Xliff` | gettext↔ICU plural conversion, `msgctxt`-as-key; XLIFF native state; mixed-format runtime end-to-end |
| 6 | `Detection` + `Analyzers` | shared detection core + the nine `APL` diagnostics + code fixes |
| 7 | `Generator` + `Reconcile` (extract) + MSBuild | in-compiler template emission (build-only, write-if-changed, atomic), typed key registry, `.props`/`.targets`, analyzer packaging |
| 8 | `Reconcile` (merge) + `Tooling` | `dotnet` tool `add` / `sync` / `convert` / `sync --check` (no `template`); the merge algorithm; full workflow sample |

### Deferred integration phases (after the core is complete)

| # | Phase | Delivers |
|---|---|---|
| 9 | Integration ✅ | Two packages: `ArchPillar.Extensions.Localization.DependencyInjection` (`AddArchPillarLocalization` — native `ILocalizer`/`ILocalizer<T>`/`DefaultLocalizer` registration, depends only on DI abstractions) and `ArchPillar.Extensions.Localization.StringLocalizer` (`AddArchPillarStringLocalizer` — the `IStringLocalizer`/`IStringLocalizer<T>`/`IStringLocalizerFactory` migration adapter: name-is-key, missing→name, positional args → `{0}`, composing over `.resx`). ASP.NET request-culture flows via `CurrentUICulture` (D-5). The core stays dependency-free; the interop package is separable and droppable (D-J). |
| 10.1 ✅ | Contracts (D-H) | `ILocalizer`/`ILocalizer<T>`/`ILocalizerFactory` modeled on `ILogger`; `[TranslationScope]`; the `Localized<TSelf>` self-scoped base (member name → key via `[CallerMemberName]`). |
| 10.2 ✅ | Category tier | `CatalogEntry.Category`; snapshot/loader tiered `culture → category → key → message`; `Localizer : ILocalizer`; `Localizer<T>` + `LocalizerFactory`; zero-alloc preserved. *(Re-pointed at the ambient store in 10.3.)* |
| 10.3 | Ambient layered store (D-I) | The `IConfiguration`-style store: layered sources, last-wins merge + atomic swap, lazy `AssemblyLoad`-driven discovery, embedded-catalog loader + advertising attribute; re-point the views at the store; test reset; trimming/single-file/AOT spike. **Amended:** the store, lookup, `ILocalizer`/`ILocalizer<T>`, and the null-renderer **stay in the runtime** (`net8.0;net9.0;net10.0`), *not* the `netstandard2.0` layer. They use `AppDomain.AssemblyLoad`, `FileSystemWatcher`, and runtime APIs the Roslyn host neither has nor needs. `Abstractions` (netstandard2.0) carries only the SPI the compiler loads — attributes, `Catalog`/`CatalogEntry`, the provider interface, the qualified-key/composite-key helpers — so the analyzer and generator can share types with the runtime without depending on a net8-only assembly (the reason `Abstractions` is kept rather than folded into the runtime). |
| 10.4 | Extractor + persistence | Category from a `[TranslationScope]`-marked generic argument found by walking the receiver's own type **and its base types and interfaces** (so `ILocalizer<T>` *and* `Localized<TSelf>`, whose scope sits on the base, resolve identically to the runtime and the Roslyn detector); `IStringLocalizer<T>` (no scope attribute) falls back to its first type argument, else global. Carry the category into the template + typed registry; format providers persist `Category`; MSBuild embeds the target catalogs. |
| 10.5 | DI + samples + docs | `AddArchPillarLocalization` feeds the ambient store and offers injectable views; samples incl. a no-DI library + an exception-text example + a `Localized<TSelf>` bundle; docs. `IStringLocalizer` adapter retained for interop. |

### Adjustment to the spec's build order

The specs build all three formats before the runtime. We pull the **runtime + ARB forward
(Phases 3–4)** so there is a working, tested vertical slice — author an `.arb` by hand, render
it with plurals — before the compile-time machinery exists. The remaining formats, the
analyzer, the generator, and the tool follow.
