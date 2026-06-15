# 00 — Architecture Overview

> Working codename for the library: **`ArchPillar.Localization`**. Rename the root namespace freely; every other spec refers to it as the root.

## Purpose

A user-interface translation library for .NET built around three ideas:

1. **Extract-from-usage.** Translatable strings are discovered from call sites by a Roslyn-based extractor, not hand-declared in a central catalog. The source-language text lives inline at the call site as a compile-time constant.
2. **Runtime-pluggable translations.** Translation files are loaded at runtime as additive overrides. An application with zero translation files runs correctly in the source language. Adding, updating, or removing a locale is a file operation, never a recompile.
3. **Standards-based handoff.** Translation files are real interchange formats that a professional or community translator can open in existing tools (primarily Poedit, optionally a Translation Management System). The library never asks the maintainer to translate anything themselves.

## The non-negotiable invariant

The in-code default message is the **terminal fallback** for every language, including the source language. Nothing on the rendering path depends on any translation file existing: a missing snapshot, culture, or key degrades to the in-code default. This invariant is what makes translations pluggable, makes partial translations degrade gracefully key-by-key, and makes "delete a locale file" a safe operation.

> **Decision D-1, as amended by D-L (see `DECISIONS.md`):** the in-code default remains the terminal fallback (the floor that needs no file), but the source language is **not** code-only. It loads as an override layer like any other culture, *above* that floor, so source wording is editable by a translator/copywriter without a recompile. The source catalog is a merged, git-tracked artifact (`extract` reconciles into it, it is not overwritten), and only a *genuine* source override — a string edited away from the in-code default — loads and ships; a bare echo of the default does not. Translation files (source and target alike) only ever override the in-code default for a specific key and culture.

## Component map

> **Packaging note:** the boxes below are *logical* components. The three `netstandard2.0` compile-time
> components — Detection, Analyzers, and Generator — ship as **one assembly**
> (`ArchPillar.Extensions.Localization.Analyzers`); code fixes ship separately because of the
> `Microsoft.CodeAnalysis.Workspaces` boundary (see Dependency policy). The diagram shows how the concerns
> depend on each other, not the assembly count.

```
                         ArchPillar.Localization.Detection        (netstandard2.0, pure)
                          - finds translatable call sites
                          - resolves key / default / context / placeholders
                          - the ONLY place "what is translatable" is defined
                                   |                |                 |
            consumed by           |                |                 |   consumed by
                                   v                v                 v
        ArchPillar.Localization.Analyzers   ArchPillar.Localization.Generator   ArchPillar.Localization.Tooling
         (netstandard2.0)                    (netstandard2.0, IIncrementalGenerator)   (dotnet tool)
         - DiagnosticAnalyzer                - emits the source-language          - add <lang> / sync / convert
         - live in-editor diagnostics          TEMPLATE only, as a build            target files ON DEMAND
         - NO disk writes                      side effect (like the doc XML)     - --check for CI
                                             - never touches target files        - template out-of-band (CI)
                                             - optionally emits code (typed keys) - the ONLY way languages are
                                                                                    added (never at build time)
                                                       |                 |
                                                       | both read/write via the same providers
                                                       v                 v
                                          ArchPillar.Localization.Formats.*
                                          - .Po      (Portable Object)
                                          - .Xliff   (XLIFF 2.1)
                                          - .Arb     (Application Resource Bundle / JSON)
                                          - all implement one provider interface over a
                                            format-neutral in-memory Catalog model
                                                                 |
                                                                 | shared message grammar
                                                                 v
                                          ArchPillar.Localization.MessageFormat
                                          - ICU MessageFormat parser + AST + formatter
                                          - embedded Unicode CLDR plural-rule data

        ArchPillar.Localization            (runtime, the thing apps reference)
         - loads catalog files via Formats.* providers
         - lock-free lookup, fallback chain, optional hot reload
         - renders via MessageFormat

        ArchPillar.Localization.Abstractions  (netstandard2.0)
         - the [Translatable] / [TranslationDefault] / [TranslationContext] attributes
         - the format provider interface + the Catalog model
         - referenced by everything; depends on nothing
```

### Why detection is its own assembly

The analyzer and the compile-time engine must agree, byte-for-byte, on what counts as a translatable call. If that logic is duplicated, the editor will flag things the extractor misses and vice versa. So the detection logic lives in one `netstandard2.0` assembly that the analyzer, the generator, and the optional tool all reference. The analyzer adds diagnostics on top; the generator and tool add disk input/output on top; none re-implement detection.

### Why the compile-time engine is a source generator

The strongly-typed **key registry** is a deterministic build output of the source — exactly like the C# documentation XML file. The Roslyn component that runs *inside the compiler during a normal build* and holds the `Compilation` (and therefore the semantic model needed for attribute-driven detection) is a source generator, so that is where the registry is produced. It emits the registry through the terminal output step (`RegisterSourceOutput`) as **in-assembly source** — the output a generator is built for — and does no file I/O.

The **source-language template, by contrast, is not the generator's job** — it is the tool's (Decision **D-K**). A source generator only ever sees the original user source, never another generator's output, so strings that exist only in Razor/Blazor/MVC generated code are invisible to it. Reading the **built assembly's IL** instead is complete by construction — every generator's output is compiled in — which is why extraction lives in the `dotnet` tool. The build runs that extraction for you after a real build (spec 06); the tool's write is atomic (temp-then-move) and skips an unchanged file, so a no-op build touches nothing, and an assembly with no strings yields no file. Moving extraction out of the generator also sidesteps the speculative host-sandboxing risk (Decision D-10): the tool is a normal console app, free to do the I/O a generator cannot.

### The `dotnet` tool

`ArchPillar.Localization.Tooling` owns the template and everything downstream of it, all on demand and none of it a build decision: `extract` builds the template from a built assembly's IL, `add <lang>` creates a new target file from the template, `sync` reconciles existing target files against it, `convert` re-serializes into another format, and `sync --check` is the continuous-integration gate. Adding a language is the only place a target language enters — explicit, human-driven, never at build time. For Portable Object, Poedit's native "create from POT" / "update from POT" are equivalent to `add`/`sync`, so a translator can self-serve; XLIFF and ARB cannot self-bootstrap, which is why the tool provides it for them. (See spec 02.)

### What the generator emits

1. **The source-language template** (primary): the language-neutral extraction — every key, source text, and metadata, no target translations — written as the build side effect (doc-XML model). This is the generator's only filesystem responsibility. It creates no target-language file, requires none to exist, and never edits one.
2. **Code** (ergonomics): a strongly-typed key registry (`const` per discovered key) so call sites and the analyzer get autocomplete and rename-safety (Decision D-4). Typed accessor methods are an available extension (open sub-decision).

Everything downstream of the template — creating a target language, syncing target files against the template, converting between formats — is the tool's job, run on demand (see spec 02). Languages are therefore never a build or developer decision (Decision D-12).

## Detection contract (summary; full detail in spec 01)

A call is translatable when an argument is bound to a parameter marked `[Translatable]`. The detection core, given a `Compilation`, yields one record per translatable call site:

| Field | Source | Required |
|---|---|---|
| `Key` | constant argument bound to the `[Translatable]` parameter | yes |
| `DefaultMessage` | constant argument bound to the `[TranslationDefault]` parameter | yes |
| `Context` | constant argument bound to the `[TranslationContext]` parameter | optional |
| `Comment` | leading comment trivia / `[TranslationComment]` constant | optional |
| `Placeholders` | parsed from `DefaultMessage` (ICU MessageFormat) | derived |
| `SourceReference` | file path + line of the call | yes |

Because detection keys off attributes through the semantic model (not a hardcoded method name), any consumer can build an ergonomic wrapper API; as long as the wrapper forwards constants to attributed parameters, the tooling follows it.

## The format-neutral Catalog model (summary; full detail in spec 03)

The reconciler and the runtime never touch a file format directly. They operate on an in-memory `Catalog` (a culture plus an ordered set of `CatalogEntry`). Each format provider serializes/deserializes that model and **declares its capabilities** (does it support obsolete entries, an explicit translation-state, native vs ICU plurals, context, comments, source references). The reconciler reads capability flags to decide how to represent state in each format. This is the single abstraction that lets Portable Object, XLIFF, and ARB coexist behind one pipeline.

## Glossary (acronyms spelled out)

- **Roslyn** — the .NET Compiler Platform; the Application Programming Interface for analyzing and transforming C# source.
- **Application Programming Interface (API)** — the set of types and methods a consumer calls.
- **Abstract Syntax Tree (AST)** — the parsed tree representation of source or of a message-format string.
- **Intermediate Representation (IR)** — here, the format-neutral Catalog model used between extraction and serialization.
- **International Components for Unicode (ICU)** — the Unicode project whose MessageFormat grammar we adopt for message values (placeholders, plural, select).
- **Unicode Common Locale Data Repository (CLDR)** — the Unicode dataset that defines, among much else, the plural-category rules per language. The source of the embedded plural-rule data.
- **Portable Object (PO)** — the GNU gettext editable translation format; file extension `.po`.
- **Portable Object Template (POT)** — the language-neutral gettext template; file extension `.pot`.
- **Extensible Markup Language Localization Interchange File Format (XLIFF)** — the OASIS standard interchange format for translation; file extension `.xliff`. We target version 2.1.
- **Application Resource Bundle (ARB)** — Google's JavaScript Object Notation-based localization format with metadata and native ICU MessageFormat values; file extension `.arb`.
- **Extensible Markup Language (XML)** — the markup language underlying XLIFF.
- **JavaScript Object Notation (JSON)** — the data format underlying ARB.
- **Base Class Library (BCL)** — the standard .NET runtime library (e.g., `System.Text.Json`, `System.Globalization`).
- **Translation Management System (TMS)** — a hosted localization platform (e.g., Weblate, Crowdin, Lokalise).
- **Dependency Injection (DI)** — the .NET pattern for supplying services; the runtime integrates with `Microsoft.Extensions.DependencyInjection` optionally.

## Dependency policy

- **Compile-time components ship as one assembly.** Detection, the analyzer, and the generator are a single `netstandard2.0` assembly, `ArchPillar.Extensions.Localization.Analyzers` — they share the detection core and one `Microsoft.CodeAnalysis.CSharp` dependency profile, and load into a single analyzer context (fewer DLLs in `analyzers/dotnet/cs`, fewer load-failure surfaces). Code fixes stay in a separate `…CodeFixes` assembly because `CodeFixProvider` needs `Microsoft.CodeAnalysis.Workspaces`, which the command-line compiler does not load. `Abstractions` (`netstandard2.0`) is referenced by both. No third-party runtime dependencies beyond `Microsoft.CodeAnalysis.*` (provided by the analyzer host).
- `MessageFormat`: no third-party dependencies. The CLDR plural data is an **embedded, version-pinned data input generated into source at build time** — a data dependency, not a package dependency.
- `Formats.Po`, `Formats.Xliff`: hand-rolled parsers, no third-party packages. XLIFF uses `System.Xml` from the Base Class Library; ARB uses `System.Text.Json` from the Base Class Library.
- `ArchPillar.Localization` (runtime): no third-party dependencies. Optional, separately-packaged adapter for `Microsoft.Extensions.DependencyInjection` and `Microsoft.Extensions.Localization` (`IStringLocalizer`) if interop is wanted (deferred, D-5).

## Decision Log

Each decision uses: **Context → Decision → Consequence**.

**D-1 — Source-language ownership.** *Decision (resolved; amended by D-L in `DECISIONS.md`):* the in-code default is the terminal fallback, so the runtime never *needs* a file for the source language. *Amendment (D-L):* the source language is nonetheless editable — its catalog loads as an override layer above the in-code default and is a merged, git-tracked artifact rather than a disposable compile-time output. *Consequence:* a render never depends on a file existing, yet source wording can be corrected without a recompile, and only genuine source overrides (not echoes of the default) load and ship.

**D-2 — Key model.** *Context:* gettext uses source-text-as-key; that orphans translations on any source edit and collapses identical strings across contexts. *Decision:* stable symbolic key + separate in-code default + optional context. *Consequence:* editing the default does not change the key; it marks existing translations *stale* (a recoverable, detectable state) rather than losing them. Fingerprinting (spec 02) implements staleness.

**D-3 — Detection mechanism.** *Decision:* attribute-driven (`[Translatable]` et al.) via the semantic model, not method-name matching. *Consequence:* consumers can wrap the call API; the constant-argument requirement is enforced by an analyzer diagnostic.

**D-4 — Code generation.** *Decision:* in scope. The generator emits a strongly-typed key registry (`const` per discovered key) for autocomplete and rename-safety. Typed accessor methods (derived from each message's placeholders) are an available extension pending the authoring-pattern sub-decision. *Consequence:* developers get symbolic, refactor-safe keys; the analyzer validates against the registry.

**D-5 — `IStringLocalizer` / Dependency Injection interop.** *Decision:* deferred to an optional adapter package. *Consequence:* core runtime has no `Microsoft.Extensions.*` dependency; ASP.NET Core users can opt into an adapter later.

**D-6 — Message value grammar.** *Decision:* ICU MessageFormat for all formats. XLIFF and ARB store ICU verbatim; the Portable Object provider converts between ICU plural and gettext native plural on read/write. *Consequence:* one rendering engine; the conversion complexity is isolated to the Portable Object provider.

**D-7 — Plural data.** *Decision:* embed Unicode CLDR plural-rule data, generated into source from a checked-in, version-pinned CLDR data file. *Consequence:* correct plurals for any CLDR language with no per-language hand-maintenance and no runtime package dependency.

**D-8 — Container formats shipped.** *Decision:* Portable Object (simple/community), XLIFF 2.1 (professional), ARB (the JavaScript Object Notation answer; spec-backed and ICU-native). Generic untyped JavaScript Object Notation is **not** shipped — it has no standard schema. The **default authoring format is XLIFF**: source and translation are distinct first-class fields (no source-as-metadata an editor can drop), it carries a native translation-state machine on top of ICU values, and it opens cleanly in Poedit/Lokalize and the management systems professional vendors expect. ARB is the lightest clean-mapping ICU-native format (key = JSON key, no XML weight) and the best choice when those properties matter; Portable Object is simplest but has a non-ICU plural model and the `msgctxt`-as-key mapping. *Separately,* the **published bundle defaults to ARB** (a distinct knob): a runtime bundle reads only the translation, so the most compact, compressible container wins — a minified ARB bundle gzips to ~60% of the XLIFF equivalent, which still carries the redundant source. *Consequence:* three providers behind one interface; the maintainer picks per project, and `convert` (D-13) makes the choice low-stakes.

**D-9 — Hot reload.** *Decision:* opt-in, off by default. *Consequence:* core lookup has zero dependency on file watching; enabling it adds one debounced `FileSystemWatcher` that calls the existing reload path.

**D-10 — Compile-time engine is a source generator that writes files.** *Context:* the catalog must be a compile-time output (doc-XML model) and detection needs the semantic model, which only a generator has in-build. *Decision:* an `IIncrementalGenerator` writes catalog files from its terminal output step; pipeline transforms stay pure (for caching), the output step does the I/O — this is the generator's purpose, not a purity violation. Churn is controlled by write-if-changed; build-only writes are an optional `DesignTimeBuild` gate. *Consequence:* "just build" updates the catalogs, no separate step. Two cache-driven behaviours to design around: the write also fires during IDE editing (gate if undesired), and it fires on code change rather than file change (a hand-deleted catalog is not regenerated until code changes — the `dotnet` tool covers that). The only speculative risk is a future host sandboxing generator file I/O, insured by the tool.

**D-11 — Obsolete keys are deleted.** *Context:* the catalog should reflect current code exactly and stay fresh. *Decision:* when a key disappears from code it is removed from every catalog file; no retention, no obsolete markers, no `--prune` step. *Consequence:* clean files and clean diffs. The one tradeoff: a *key rename* is delete-plus-add, so its translation is dropped at that moment; this is safe given git-tracked files (recoverable from history) and translation memory (re-suggests by source text). Drift (same key, changed default) is unaffected — the translation is kept and marked needs-review; only a vanished key is deleted.

**D-12 — Target languages are added on demand, never at build time.** *Context:* a language is an operations/translation decision, often made long after the code shipped and never by a developer editing the project. *Decision:* the build emits only the source-language template; there is no declared-language input. A target language enters solely by an explicit, human-driven `tool add <lang>` (or Poedit's "create from POT" for Portable Object). The generator never creates, reads, or touches a target file. *Consequence:* enabling a language requires no recompile, no project edit, no developer involvement; the set of languages lives entirely in the file system, discovered at runtime.

**D-13 — Format conversion is a tool capability.** *Context:* a project works in one format but may need to hand a different one to a particular vendor, or migrate formats. *Decision:* `tool convert` re-serializes the template or any target file between Portable Object, XLIFF, and ARB over the shared `Catalog` model. *Consequence:* conversion is nearly free (read one provider, write another); conversions through Portable Object run the ICU↔gettext plural conversion (spec 03), and capability-flag differences are reported as losses rather than failing silently.

**D-14 — Runtime loads all formats present, prefers by fidelity.** *Context:* a project may end up with mixed formats (e.g. an ARB project receiving an XLIFF back from a vendor). *Decision:* the runtime loads every translation file in the directory regardless of format, grouping by culture; on a per-key conflict across formats it prefers the higher-fidelity format via a configurable precedence defaulting to ICU-native (XLIFF, ARB) over Portable Object. *Consequence:* formats mix freely at runtime; the precedence only bites on genuine overlap, and because XLIFF and ARB are equally faithful for runtime values their relative order rarely matters.

## Open questions to settle before / during build

- **Typed accessors (sub-decision of D-4):** beyond the key registry, should the generator emit typed accessor methods (e.g., `Strings.Hello(string name)`)? If yes, what is the authoring pattern — purely call-site-driven (the generator infers a method name and parameter list from the inline default's placeholders) or a light declaration? The key registry ships regardless; this is additive.
- **Key naming policy (non-blocking):** free-form strings, or an enforced convention (e.g., `area.element.purpose`)? An analyzer rule can enforce a configured pattern if wanted.
- **Per-project vs per-locale format (non-blocking):** is the container format chosen once per project, or may different locales use different formats? The Catalog model supports the latter; confirm whether the tooling should expose it.
- **Catalog file layout (non-blocking):** one file per locale (`messages.de.po`) versus a per-locale subdirectory (`de/messages.po`). Affects culture discovery (spec 05) and the generator's `AdditionalFiles` globs.

## Build order recommendation for the Claude Code session

1. `MessageFormat` (spec 04) — pure, no dependencies, unblocks everything that renders or validates messages.
2. `Abstractions` (attributes + Catalog model + provider interface; specs 01 & 03 headers).
3. `Detection` + `Analyzers` (spec 01).
4. `Formats.Po`, then `Formats.Xliff`, then `Formats.Arb` (spec 03).
5. The extract + reconcile core (spec 02), in the tool.
6. `Generator` (the `IIncrementalGenerator`: the typed key registry); `Tooling` (the `dotnet` tool: `extract` / `add` / `sync` / `convert` / `merge`).
7. `ArchPillar.Localization` runtime (spec 05).
