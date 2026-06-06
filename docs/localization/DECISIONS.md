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
| `ArchPillar.Localization.Detection` | `ArchPillar.Extensions.Localization.Detection` |
| `ArchPillar.Localization.Analyzers` | `ArchPillar.Extensions.Localization.Analyzers` |
| `ArchPillar.Localization.Generator` | `ArchPillar.Extensions.Localization.Generator` |
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
and it duplicates `archpillar-loc sync --check`, which detects drift canonically. Staleness is therefore
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
| 4 | **Runtime** `Localization` | `Localizer`, fallback chain, lock-free snapshot, loading + precedence, hot reload, static helper — first usable slice (hand-authored ARB) + sample |
| 5 | `Formats.Po` + `Formats.Xliff` | gettext↔ICU plural conversion, `msgctxt`-as-key; XLIFF native state; mixed-format runtime end-to-end |
| 6 | `Detection` + `Analyzers` | shared detection core + the nine `APL` diagnostics + code fixes |
| 7 | `Generator` + `Reconcile` (extract) + MSBuild | in-compiler template emission (build-only, write-if-changed, atomic), typed key registry, `.props`/`.targets`, analyzer packaging |
| 8 | `Reconcile` (merge) + `Tooling` | `dotnet` tool `add` / `sync` / `convert` / `sync --check` (no `template`); the merge algorithm; full workflow sample |

### Deferred integration phases (after the core is complete)

| # | Phase | Delivers |
|---|---|---|
| 9 | Integration ✅ | `ArchPillar.Extensions.Localization.DependencyInjection`: `AddArchPillarLocalization`, the `IStringLocalizer`/`IStringLocalizer<T>`/`IStringLocalizerFactory` adapter (name-is-key, missing→name, positional args → `{0}`), and ASP.NET request-culture via `CurrentUICulture` (D-5). The core stays dependency-free. |

### Adjustment to the spec's build order

The specs build all three formats before the runtime. We pull the **runtime + ARB forward
(Phases 3–4)** so there is a working, tested vertical slice — author an `.arb` by hand, render
it with plurals — before the compile-time machinery exists. The remaining formats, the
analyzer, the generator, and the tool follow.
