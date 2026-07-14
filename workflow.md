# Localization Libraries Review — Workflow

> Living plan for a package-by-package review of the `ArchPillar.Extensions.Localization`
> family, in dependency order (leaves first). We apply a fixed set of **review rules** to each
> assembly, tick the matrix as each lens is applied, and log findings as **What we found /
> How we fixed it**. This file is intentionally untracked.

## How we work this

- Review in **dependency order**: an assembly is understood before its consumers.
- **Finish each assembly before starting the next: review → agree findings → fix → green → next.**
  Root-first: fixing a foundation before reviewing its consumers keeps the downstream reviews clean
  and avoids logging findings that would evaporate once the root is fixed.
- Per assembly: read every non-generated file → apply each review rule (R1–R8) → tick the matrix →
  log findings → agree fixes → apply → keep build green (zero warnings) → run tests.
- **Before changing any *public* surface of a leaf, do a quick usage scan** (grep call sites across
  the monorepo) so the fix is informed by real consumer usage. Internal-only changes skip this.
- Batch only *cosmetic / cross-cutting patterns*: note them per assembly, fix the pattern in one
  stroke at the assembly that owns it.
- **Code changes only after a finding is agreed.** Small, reversible steps.
- **Fixes now, gaps later.** During an assembly's pass we apply *fixes* only. Net-new **feature gaps** are
  logged under *Open questions* and tackled in a dedicated pass at the **end** of the review — not mid-stream.
- A finding is anything worth a decision, not only a defect.
- If a later finding implies an earlier assembly needs another look, record it in
  **Retroactive review backlog** and re-open that assembly's affected rule in the matrix.

### Severity legend
- 🔴 **Bug / correctness** — wrong behavior, or contradicts a SPEC guarantee.
- 🟠 **Design / principle** — KISS/YAGNI/SRP/one-door violation, leaked internal, dead machinery.
- 🟡 **Readability** — naming, structure, comments, clarity; no behavior change.
- 🟢 **Note / question** — needs a decision or confirmation before it becomes an actioned finding.

### Status / checkbox legend
- Matrix cell `[ ]` = lens not yet applied · `[x]` = applied (findings, if any, are logged) · `—` = N/A.
- Assembly status: `not started` · `in progress` · `reviewed` · `fixes pending`.

---

## Review rules (the lenses)

Each is applied to every assembly; a ticked box means "we looked through this lens here", not
"it passed" — anything found is in the findings table.

- **R1 — Correctness & SPEC guarantees.** No bugs; the terminal-default / never-fail-at-runtime and
  fail-fast-at-build semantics hold; edge cases sound. For compile-time pieces: generator
  determinism, incremental correctness, no analyzer exceptions.
- **R2 — KISS.** The simplest thing that meets the requirement; no incidental complexity or
  cleverness the problem didn't ask for.
- **R3 — YAGNI / subtraction.** No speculative machinery, dead code, or unused extension points;
  the change that removes more and reads clearer is preferred.
- **R4 — One job, one owner / one door.** Each type and method does one thing; each fact or decision
  has exactly one owner; exactly one path per concern (no parallel mechanisms, no duplication).
  **Placement follows ownership** — a type specific to one format/feature belongs *with* that
  format/feature, not in a shared/abstractions layer, even when it is internally cohesive. When a type
  is found to be X-specific, immediately ask whether it lives in the right assembly. [sharpened after ABS-7]
- **R5 — Encapsulation & boundaries.** Expose intent (verbs), not the internal data shape; correct
  accessibility and sealing; no leaked internals; no global/static registries or ambient mutable
  state beyond the one sanctioned store (D-I).
- **R6 — Readability & style.** Naming (no abbreviations), file-scoped namespaces, `var` rules,
  Allman braces, no needless comments, XML docs on public surface, analyzer-clean; reads like its
  neighbors.
- **R7 — SPEC/doc consistency.** Code matches `SPEC.md`, the numbered specs, and `DECISIONS.md`;
  any drift is recorded and reconciled.
- **R8 — Platform & dependencies.** Target-framework limits respected; dependencies warranted
  (core runtime = BCL only; Roslyn/compile-time = `netstandard2.0`; adapters = only their host SDK).

---

## Dependency graph (review order)

```
Tier 0  Abstractions ───┐         MessageFormat ──┐
            │           │              │           │
Tier 1      │        Analyzers ◄───────┤      CodeFixes ◄─┘ (MessageFormat)
            │        (Abstractions+MessageFormat)
            │           │
Tier 2  Localization (core) ◄── Abstractions + MessageFormat  [+ Analyzers/CodeFixes build-only]
            │
Tier 3  ┌───┼─────────────┬───────────────┬──────────────┐
     DependencyInjection  AspNetCore   WebAssembly     Tooling
            │
Tier 4  StringLocalizer ◄── Localization + DependencyInjection
```

---

## Review matrix

Rows in dependency order. Tick R1–R8 as each lens is applied to that assembly.

| Assembly (LOC) | R1 | R2 | R3 | R4 | R5 | R6 | R7 | R8 | Status |
|----------------|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|--------|
| Abstractions (596)         | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | ✅ reviewed — fixed (incl. ABS-7 placement; 276 tests green) |
| MessageFormat (2064)       | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | ✅ reviewed — MF-1/3ab/4/5/7/9 fixed; MF-3c optional; MF-6/8 deferred (gaps) |
| Analyzers (1404)           | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | ✅ reviewed — ANL-1/2/3/4/5 all fixed |
| CodeFixes (117)            | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | ✅ reviewed — clean (CF-1 cosmetic, no action) |
| Localization core (3769)   | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | ✅ reviewed (all 33) + deep catalog-subsystem restructure — LOC-1/2/3/4/5, CS-1/2/3/4 fixed (loader lock-free, ProviderState/ProviderCatalogs deleted, Cultures allow-list implemented); LOC-6 done in the cycle-2 gaps pass |
| DependencyInjection (59)   | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | ✅ reviewed — LOC-1 (rename) + LOC-2/B1 (context-as-factory, wired) done |
| AspNetCore (205)           | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | ✅ reviewed — clean; ASP-1 (generic category parity) fixed |
| WebAssembly (45)           | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | ✅ reviewed — clean, no findings |
| Tooling (1795)             | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | ✅ reviewed — TOOL-1 (Spectre.Console.Cli command split) + TOOL-2..5 (extractor split, single module read, comment + one-owner fixes) |
| StringLocalizer (220)      | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | `[x]` | ✅ reviewed — clean; SL-1 (public CategoryName) done |

---

## Retroactive review backlog

When a finding in a later assembly means an earlier one needs another look, log it here and
re-open the relevant matrix cell(s).

| ID | Raised in | Re-check rule | Earlier assemblies to revisit | What to look for | Status |
|----|-----------|:-------------:|-------------------------------|------------------|--------|
| _(none yet)_ | | | | | |

---

## Findings

One subsection per assembly. File inventory first, then a findings table. Pre-observations from the
initial mapping are seeded into their relevant assemblies as 🟢 questions to verify in context.

### Abstractions
**Files:** Catalog · CatalogEntry · CatalogWriteOptions · QualifiedKey · TranslationKey · TranslationState ·
SourceReference · FormatCapabilities · ITranslationFormat · TranslationFormatRegistry · TranslationAttributes ·
TranslationMarkers · LocalizedAnnotationAttributes · LocalizationCatalogAttribute ·
LocalizationSatelliteCatalogsAttribute · Internal/Polyfills · Internal/SetsRequiredMembersPolyfill

| ID | Sev | Where | What we found | How we fixed it |
|----|-----|-------|---------------|-----------------|
| ABS-1 | 🟢 | SPEC.md + features.md + skill ref `di-runtime-and-interop.md` + TODO.md | **Confirmed.** `ITranslationSource` is absent from all `src/**` code (removed in `3f1b794`) but still documented as a live extension point in the SPEC API table, `features.md`, and the skill reference. SPEC/code drift (R7). | **Fixed (SPEC).** Removed `ITranslationSource` from the SPEC API table → "the catalog model and the format extension point". **Deferred** (see below): the 4 `features.md` refs + the skill ref describe the runtime provider model that replaced it → reconcile during the core **Providers** review. `TODO.md` left (historical completed item). |
| ABS-2 | 🟢 ✅ | TranslationFormatRegistry.cs:8 | **Resolved — not a violation.** Instance type, no static state, constructed per-consumer (core providers + `LocalizerOptions`, Tooling `ToolApplication`); the doc explicitly defends the name. Complies with R5. | _No action — closed as clean._ |
| ABS-3 | 🟠 | TranslationKey.cs ↔ TranslationAnalyzer.cs:151, Reconciler.cs:189 | The **category-qualified** composite key (`category ␄ context ␄ key`) is assembled by hand — `entry.Category + TranslationKey.Separator + TranslationKey.Compose(…)` — duplicated identically in the analyzer and the reconciler. `TranslationKey` owns only the key+context half. The SPEC requires analyzer/generator/tool to agree **byte-for-byte**; keeping them in sync by copy-paste is fragile (R4 one-owner; borderline R1). | **Fixed.** Added `TranslationKey.ComposeQualified(category, key, context)` as the one owner; rerouted `TranslationAnalyzer.cs:151` and `Reconciler.cs:189`. Verified the generator groups by category (builds no composite — no 3rd site) and the runtime snapshot uses the within-category `Compose` by design. Output byte-identical; Detection + Tooling + round-trip tests green. (`textKey` at analyzer:152 is a distinct single-use composite, left as-is.) |
| ABS-4 | 🟡 | ITranslationFormat.cs:38 | `WriteAsync(Stream, Catalog, CancellationToken, CatalogWriteOptions? = null)` places `CancellationToken` in the middle (required) and the optional `options` last — inverting the .NET convention (token last, usually defaulted) (R6). | **Fixed.** Reordered to `(output, catalog, options = null, cancellationToken = default)` — token last & optional. Updated the interface, 3 implementers (Arb/Xliff/Po), the `ToolApplication` caller, 13 test call sites + 1 test stub, and the doc `03` snippet (also corrected its stale async `Read`). Format round-trip tests green. |
| ABS-5 | 🟢 | QualifiedKey.cs:59 | `Unqualify` strips a leading `::` from **any** global key, so a global key literally beginning with `::` would not round-trip — it collides with the `@`-escape form `::@key` (R1 corner case). | **Fixed (documented).** Added to `QualifiedKey`'s doc: keys are stable symbolic identifiers and never begin with the `::` separator, so a bare key is never ambiguous with a qualified/escaped member. |
| ABS-6 | 🟡 | QualifiedKey.cs:4, TranslationKey.cs:4 | Both types open their XML doc with the identical phrase *"The single convention for combining a Key and optional Context"*, reading as two owners of one concern though they own different conventions (on-disk identity incl. category vs. runtime composite) (R6 clarity). | **Fixed.** `TranslationKey` doc now states it owns the in-memory composites (within-category `Compose` + cross-category `ComposeQualified`); `QualifiedKey` doc reframed as the human-facing on-disk identity. Each cross-references the other. |
| ABS-7 | 🟠 | QualifiedKey (was in Abstractions) → ArbMemberKey (core Formats) | **Missed in the first R4 pass — caught by the reviewer.** `QualifiedKey` is ARB-specific (used only by `ArbTranslationFormat`, no other consumer, no direct tests) yet lived in the format-neutral Abstractions assembly, and its name didn't advertise that. Placement + name contradict assembly ownership (R4/R5): Abstractions owns the contracts every format shares; ARB's member-naming scheme is ARB's alone. | **Fixed.** Moved to [src/Localization/Formats/ArbMemberKey.cs](src/Localization/Formats/ArbMemberKey.cs) and **renamed `QualifiedKey` → `ArbMemberKey`** (parallels `TranslationKey`, advertises the ARB-specificity); `public`→`internal` (ARB impl detail, not in the SPEC surface); dropped the now-dead `#if NETSTANDARD2_0` branches (core is net8+). Updated the 2 ARB call sites + doc `03`; removed the cross-assembly cref from `TranslationKey`. Build green; 276 tests pass. |
| — | 🟢 | ITranslationFormat (sync `Read`/async `Write`); Internal/Polyfills split across 2 files | **Minor notes, likely accept.** Read-sync/Write-async is defensible (read parses an in-hand stream). Polyfills split by target namespace — could be one file; trivial. | _No action proposed._ |
| — | 🟢 | Abstractions placement re-scan | After ABS-7, re-applied the R4 placement lens to **every** Abstractions type: catalog model (`Catalog`/`CatalogEntry`/`CatalogWriteOptions`/`TranslationState`/`SourceReference`/`FormatCapabilities`), the format contract (`ITranslationFormat`/`TranslationFormatRegistry`), the in-memory key composite (`TranslationKey`), and the detection/annotation attributes are all genuinely format-neutral and shared by ≥2 consumers. | _No action — `QualifiedKey` was the sole misplacement._ |

### MessageFormat
**Files:** MessageParser · MessageSyntax · MessageAst · MessageFormatter · PluralRules · PluralOperands ·
MissingArgumentPolicy · MessageFormatError · MessageFormatException · MissingArgumentException ·
Internal/MessageGrammarParser · Internal/MessageRenderer · Internal/OtherBranchInserter · Internal/CldrPluralRule ·
Internal/CldrRuleEvaluator · Internal/GettextPluralExpression · Internal/CldrPluralData.g (generated) · Internal/Polyfills

**Static-class survey (the user's "too many statics" concern):** 8 `static class` total → 2 non-design
(`IsExternalInit` polyfill, `CldrPluralData.g` generated data). Of the 6 hand-written: `PluralRules`,
`CldrRuleEvaluator`, `GettextPluralExpression` and the `MessageSyntax`/`MessageParser` facades are
**truly standalone pure-function modules** over generated CLDR data — the "web" among them is pure
composition `f(g(h(x)))`, no shared mutable state, so keeping them static is idiomatic (DI-ifying pure
CLDR math would be speculative — YAGNI). `MessageRenderer` is the one exception. The stateful part of the
pipeline (cache + policy) correctly lives on the `MessageFormatter` **instance**, not in statics.

| ID | Sev | Where | What we found | How we fixed it |
|----|-----|-------|---------------|-----------------|
| MF-1 | 🟠 | Internal/MessageRenderer.cs | The static-web concern, localized: `MessageRenderer` (23 static members — the biggest) is **not** standalone. It threads a 5-part render context `(builder, culture, arguments, policy, pound)` through ~10 static methods — a class wanting to exist. `culture`/`arguments`/`policy`/`builder` are render-constant (→ fields); only `pound` varies with recursion (→ stays a param). | **Done (`readonly ref struct`).** `static class` → `readonly ref struct` whose fields hold the render-constant context (`_builder`/`_culture`/`_arguments`/`_policy`); the recursive walk now threads only `pound`. **First cut used a `sealed class` — reverted: a class adds a per-render *heap* allocation on a deliberately zero-alloc path (the reviewer caught it). A `ref struct` is stack-only, so the context grouping costs no allocation and the compiler enforces "never escapes" (render is synchronous — no lambda/async/yield); same tool as MF-4.** Kept the `static Render` entry for the literal fast-path + thread-local builder pool. Pure helpers (style mappers, `TryToNumber`, `TryGetLiteral`, branch lookups, `EmptyMessage`) stayed `static`. `Render` signature unchanged → no caller touched. Build green (0 warnings); 104 MessageFormat + 135 core tests pass. |
| MF-2 | 🟢 | CldrRuleEvaluator.cs:13; MessageRenderer.cs:18 | Two static **mutable** caches. `_cache` (compiled CLDR predicates) is keyed by the **finite** set of CLDR condition strings (from generated data, never user input) → bounded, thread-safe; `[ThreadStatic] _pooledBuilder` is thread-local. Both defensible perf, but they are exactly the "global state" the repo is wary of — a conscious note. | _Note; no change proposed (raise only if a test-isolation/reset need appears)._ |
| MF-3 | 🟠 | MessageRenderer.cs (doc) · PluralRules.Operands · PluralRules.CultureCandidates | **The "dynamic render allocates only the result" claim is optimistic on the plural path.** Literal-only render IS zero-alloc ✓. But a **plural/number** render allocates intermediates: `PluralRules.Operands` does `decimal.ToString().Split('.')` + `TrimEnd`/`PadRight`, and `CultureCandidates` is a `yield` iterator + `culture.Split('-')`. (The dict `Format` overload's `ToArray` is not just off the hot path — it has **no caller at all**; see MF-7.) **No functionality changes** in any fix — identical plural output and culture fallback. | **3a + 3b done.** **(a)** Corrected the renderer's allocation doc (literal = zero-alloc; plural/number allocates intermediates). **(b)** Replaced the allocating `CultureCandidates` (`yield` + `Split` every call) with one `RulesFor(table, culture)` helper — also consolidates the culture-fallback into a single owner and drops the redundant `CardinalRules` wrapper; only a base-language substring allocates, and only on the fallback path. Build green; 363 tests pass. **(c) remaining/optional:** `Operands` via `decimal.GetBits` (correctness-sensitive) — separate bite or defer. |
| MF-4 | 🟡 | Internal/MessageGrammarParser.cs | The parser is built inline (`new MessageGrammarParser(text).ParseFull()`), never escapes, and has no lambdas/`yield`/async and no field holds it → it **can be a `ref struct`**, removing the per-parse parser-object allocation and making "stack-only, never escapes" a compiler-enforced invariant (and enabling span-based input). Caveat: the parser object is the *smallest* parse allocation — each `ReadIdentifier`/`ReadKeyword`/`ReadInteger`/`ReadStyle` news up a `StringBuilder`; `ReadInteger` could parse straight from the span with zero alloc, identifiers/keywords could be `Substring`. Parse is the **cold** path (once per template, cached), so this is discipline, not hot-path. | **Done (`ref struct`).** Converted `sealed class` → `ref struct`; marked the 5 non-mutating members `readonly`. Build green; MessageFormat 104 tests pass. **Deferred** (separate optional bite): the span-based token reads (zero-alloc `ReadInteger`, `Substring` identifiers/keywords). |
| MF-5 | 🟠 | Internal/OtherBranchInserter.cs ↔ Internal/MessageGrammarParser.cs | **A second scanner duplicates the grammar.** `OtherBranchInserter` re-implements the brace / whitespace / apostrophe-quote scanning rules (`ScanMessage`/`ScanArgument`/`ScanBranches`/`SkipQuoted`/`ReadToken`/`Matches`) that `MessageGrammarParser` already has — because the AST is position-free, so finding each construct's closing `}` to insert ` other {}` needs a fresh scan. Its own doc admits "the same brace and apostrophe-quoting rules as the grammar." Not a bug today (only runs on already-validated text) but the two scanners must evolve in lockstep (R4 one-owner; same shape as ABS-3). | **Done (Option A — parser owns the offsets).** The grammar parser now *keeps* the close-brace offset it already computes for any construct missing `other` (lazily — `List<int>?`, so a well-formed message allocates nothing extra), exposed as `MissingOtherCloseOffsets`. `MessageParser.InsertMissingOtherBranches` parses once and splices ` other {}` at those offsets; `MessageSyntax` delegates to it. **`OtherBranchInserter` (~230 lines) deleted** → one scanner owns the grammar. AST stays position-free (offsets ride a side-channel list, not the records). User picked A over (b) shared low-level scanner / (c) accept-duplication; caveat accepted: the parser's output contract widens by one source-position side-channel, justified by deleting a whole duplicate scanner. Offsets are ascending by construction (single left-to-right pass) → splice last→first, no sort. Build green (0 warnings, full solution); 363 tests pass incl. the `InsertMissingOther*` + CodeFixes paths. |
| MF-6 | 🟡 | PluralRules.cs:72 | `minFractionDigits` on `Operands` is **unwired** (no caller) and affects plural-category **selection** operands, *not* display. The user's money-display need is served by the `currency` number style (.NET `"C"` → "$19.99"), a **different** code path — so this param is **orthogonal** to that requirement, not its enabler. | _Decoupled from the use case. Keep only if we foresee *pluralizing* forced-decimal numbers (niche, e.g. "1.00 dollars"); otherwise a clean removal. Low stakes either way. Pending._ |
| MF-7 | 🟠 | MessageFormatter.cs | **Unused public API (answers "why is `ToArray` there").** The `IReadOnlyDictionary<string, object?>` `Format` overload + its `ToArray` helper had **no caller anywhere in the repo** — the runtime uses the `params`-tuple overload and `MessageFormatterTests` only exercises the tuple overload. Published but unused **and** untested. (User: Localization is preview-only, no API-stability promise → safe to cut.) | **Done — removed.** Deleted the dict overload + `ToArray`; kept the lean `params`-tuple overload (cleaned its now-orphaned "allocation-lean overload" comment). Build green (0 warnings); 251 tests pass (104 MessageFormat + 135 core + 12 e2e). Doc signatures showing the overload reconciled (`04-message-format-and-plurals.md`). |
| MF-8 | 🟠 | MessageRenderer.FormatNumber / RenderPlural ↔ PluralRules.Operands | **Plural selection and `#` display disagree on visible fraction digits.** Selection reads the decimal's full scale (`1.50m`→`v=2`, `1.0m`→`v=1`), but `#` / default `{n, number}` uses `"#,##0.###"`, which **trims** trailing zeros (`1.50m`→"1.5", `1.0m`→"1"). So `1.0m` selects `other` yet prints "1" → "1 stars". Harmless in `en`; can mis-select where `v=1`≠`v=2` categories. Same root as the money feature: the displayed precision must drive the operands. | _Deferred (gap): fold into the fixed-fraction-digits feature (one source of precision for both display and operands). **Also — the `currency` style render path (`{x, number, currency}` → "C") is untested and must be covered.**_ |
| MF-9 | 🟡 | MessageParser.cs | **The `Message`-tree descent had three owners.** `ExtractPlaceholders` and `FindConstructsMissingOther` each hand-rolled the same recursive `foreach part → switch → recurse into plural/select branches` walk (6 private helpers between them); the renderer has a third copy. This is the real residue of the "static web" feeling — duplicated structural recursion, not the pure-function statics (`PluralRules` et al. are pure `f(x)` over pinned CLDR data → correctly static; DI-ifying them would be YAGNI). User chose "leave the pure math, dedup the walk." | **Done.** Collapsed the 6 recursion helpers into **one** private `Walk(Message, Action<MessagePart>)` — the single owner of "how to walk a message tree"; each caller now supplies only its per-node action (a `switch` with `when` guards). The renderer keeps its own hand-rolled walk (hot path — the `Walk` delegate allocates, acceptable only off the render path). No new type (stays inside `MessageParser`, static count unchanged). Behaviour identical (pre-order, first-seen). Build green (0 warnings); 363 tests pass (104 MF + 135 core + 53 detection + 12 e2e + 59 tooling). |
| — | 🟢 | exceptions · AST records · PluralOperands · CldrPluralRule · CldrPluralData.g · Polyfills | **Correctness pass — clean.** AST is immutable records (`PluralSelector` is a value-equality `record struct` → valid dict key); the two exceptions are minimal domain types carrying just the data they need; `PluralOperands` faithfully models UTS#35 (the `e`/`c` operands *are* referenced by real CLDR rules, e.g. `ca`, and default to 0 → correct non-compact behaviour); generated CLDR data is `// <auto-generated/>`, version 48. No bugs found. | _No action._ |

### Analyzers
**Files:** TranslationAnalyzer · Diagnostics · Detection/TranslationSiteDetector · Detection/LocalizedBundleClassifier ·
Detection/DetectionTypes · Generator/TranslationGenerator · Generator/TranslationKeyRegistryEmitter ·
Generator/LocalizedBundleConstructorEmitter · Generator/LocalizedBundleRegistrationEmitter ·
Generator/Internal/Fingerprint · Generator/Internal/KeyIdentifier · Generator/Internal/LocalizedBundleDetector · Internal/Polyfills

| ID | Sev | Where | What we found | How we fixed it |
|----|-----|-------|---------------|-----------------|
| ANL-1 | 🟠 | Generator/Internal/Fingerprint.cs | **Dead code — orphan of a deliberately-unbuilt feature.** `Fingerprint.Compute` has **no caller anywhere** (grep `Fingerprint.` = 0 hits; the other `Fingerprint*` matches are unrelated MSBuild StaticWebAssets props). It is the drift-detection machinery that live stale-source **APL0009** would have used — but `DECISIONS.md` **D-G** says APL0009 is *not implemented* ("staleness is the tool's job"). The one *live* fingerprint is Tooling's own `TemplateBuilder.Fingerprint` (same algorithm, separate copy, actually used). So this is speculative machinery for a cancelled feature. | **Done — deleted** `Generator/Internal/Fingerprint.cs` (YAGNI / R3). Confirms the APL0009 numbering gap is intentional (D-G). Build green; Detection 53 + e2e 12 + Tooling 59 pass. |
| ANL-2 | 🟠 | TranslationSiteDetector.DetectAt → AttributeSymbols.From | **Well-known symbols re-resolved per node on the analyzer hot path.** `DetectAt` calls `AttributeSymbols.From` → **7× `GetTypeByMetadataName`** for *every* invocation/object-creation/element-access node, on every keystroke. Roslyn's own guidance is to resolve well-known symbols **once** in a compilation-start action. The analyzer already does exactly that for the *bundle* path (symbols resolved in `OnCompilationStart`, passed into `AnalyzeBundle`) — but the far more frequent translation-site path re-resolves. `DetectCore` already takes a pre-resolved `AttributeSymbols`; only the public entry re-derives it. | **Done (analyzer path).** Added an `internal DetectAt` overload taking an already-resolved `AttributeSymbols` (the private nested type is now `internal` — analyzer + generator + detector are one assembly, so **no public surface change**). `OnCompilationStart` resolves the symbols **once** and threads them into the per-node action, so the 7 metadata lookups happen once per compilation instead of once per node/keystroke — matching the bundle path. `DetectCore` already accepted symbols, so the detection logic is untouched. **Generator left as-is by design** (its incremental `static` transform can't capture per-compilation state without a Combine; build-time is not the IDE hot path). Build green (0 warnings); Detection 53 + e2e 12 + Tooling 59 pass. |
| ANL-3 | 🟡 | TranslationKeyRegistryEmitter.cs:94 | `Unique` writes `System.Globalization.CultureInfo.InvariantCulture` fully-qualified while the same file (`EscapeLiteral`, line 132) uses the imported `CultureInfo`. Cosmetic inconsistency. | **Done** — uses the imported `CultureInfo`. Build green. |
| ANL-4 | 🟡 | TranslationAnalyzer.RecordedSite | Manual immutable class (ctor + 5 get-only props) where the house style is "records for immutable data types". A positional `record` would be terser and equal-by-value. Analyzer-local plumbing, low stakes. | **Done** — collapsed 21 lines → one positional `record RecordedSite(...)`. Build green. |
| ANL-5 | 🟠 | TranslationAnalyzer.CheckSite:151-152 | **Divergent reinvention of an existing owner (user caught it: "we already have the translation key").** The (category,key,context) key uses `TranslationKey.ComposeQualified` (one owner ✓), but the (category,**text**,context) key was built **inline** with `+ Separator +` — and in a *different shape* (context placed after the middle part + always both separators, vs `ComposeQualified`'s context-first / separator-dropped-when-empty). A second ad-hoc composite with no owner and a latent shape-drift trap. | **Done** — replaced the inline build with `TranslationKey.ComposeQualified(site.Category, site.DefaultMessage, site.Context)` (default text plays the key's role; same injectivity guarantee). One owner for both composites. APL0007 test (identical-text) covers it → green. |

_APL0009 numbering gap (0008→0010): **confirmed intentional** per DECISIONS.md D-G — not a finding. Correctness of duplicate/identical-text (APL0006/7) whole-compilation determinism, key-registry uniquification, and ICU/placeholder detection all read sound (R1 ✓)._

### CodeFixes
**Files:** MarkLocalizedPartialCodeFixProvider · MissingOtherCodeFixProvider

| ID | Sev | Where | What we found | How we fixed it |
|----|-----|-------|---------------|-----------------|
| CF-1 | 🟢 | MissingOtherCodeFixProvider.AddOtherBranchAsync | The fix rebuilds the literal with `SyntaxFactory.Literal(rewritten)`, which always emits a **regular** quoted literal. A default message written as a *verbatim* (`@"…"`) or *raw* (`"""…"""`) string would come back as a regular literal (re-escaped). The **logical value is always preserved** (round-trips through `Token.ValueText`), so this is cosmetic — never wrong code — and only on the rare non-regular default-message literal. | _No action (KISS/YAGNI): preserving the literal *kind* would need hand-rolled token reconstruction for a low-probability, cosmetic-only case. Noted so it's a conscious call, not an oversight._ |

_Reviewed R1–R8. Both providers are minimal, correct, `sealed`, `[Shared]`, batch-fixable, XML-documented, and correctly skip non-literal / non-class sites (graceful no-op). The two files share the standard Roslyn skeleton — **not** consolidated on purpose (a shared base for two ~30-line providers is over-engineering; KISS). `_ = cancellationToken;` is the accepted discard for the API-mandated token on a synchronous transform. 3 tests pass._

### Localization (core)
**Files (root):** ILocalizer · ILocalizerFactory · Localizer · LocalizerFactory · LocalizationContext ·
DefaultLocalizer · LocalizerOptions · Localized · RenderingContext · CultureLoading · EnumLocalizationExtensions
**Catalogs/:** CatalogStore · CatalogLoader · CatalogSource · CatalogDescriptor
**Providers/:** ICatalogProvider · DirectoryCatalogProvider · ManifestCatalogProvider · ResourceCatalogProvider ·
InMemoryCatalogProvider · ProviderState
**Snapshots/:** TranslationSnapshot — **Formats/:** BuiltInTranslationFormats · ArbTranslationFormat ·
XliffTranslationFormat · PoTranslationFormat · PoPluralConverter · IcuPluralScanner · SourceReferenceText
**Internal/:** AmbientLocalizer · CategoryLocalizer · CategoryName · NoOpWatch

| ID | Sev | Where | What we found | How we fixed it |
|----|-----|-------|---------------|-----------------|
| LOC-1 | 🟡 | Internal/AmbientLocalizer.cs + DependencyInjection/AmbientLocalizer.cs | Two types named `AmbientLocalizer` across core and DI. **Observed:** core's `AmbientLocalizer`(non-generic, the global bucket) + `AmbientCategoryLocalizer`/`<T>`; DI's `AmbientLocalizer<T>` is a *registrable* bridge that forwards to `context.For<T>()` (DI's open-generic registration needs a concrete public type, and core's `AmbientCategoryLocalizer<T>` is `internal`). Split is intentional, not duplication — but the shared bare name `AmbientLocalizer` is a readability trap. | **Done.** Renamed the DI bridge `AmbientLocalizer<T>` → **`InjectedLocalizer<T>`** (file too), so the name signals its role (the open-generic shim for `ILocalizer<T>` injection) and no longer collides with core's `AmbientLocalizer`. Kept as-is otherwise — the open-generic registration genuinely needs a concrete public-in-assembly type. |
| LOC-2 | 🟠 | ILocalizerFactory · LocalizerFactory · Internal/CategoryLocalizer(+`<T>`) | **A second, unused "get a category localizer" door.** The factory family has **no production caller** (grep: only `CategoryLocalizerTests` news up `LocalizerFactory`), is **not DI-registered** (so it can't be injected — unlike the `ILoggerFactory` it deliberately mirrors, D-H), and is fully superseded by `LocalizationContext.For<T>()`/`ForCategory` + `Localizer.For<T>()` (what production + DI actually use). Worse, its `CategoryLocalizer` calls `DefaultLocalizer.TranslateInCategory` **without `EnsureCulture`**, so under `OnDemand` it never triggers the culture load — **violating the documented contract** ("the first *lookup* in a not-yet-loaded culture pays a one-time read", `CultureLoading` doc). Root cause: the factory family predates the ambient-store redesign (DECISIONS 10.2 "*Re-pointed at the ambient store in 10.3*") and wasn't removed. Documented in D-H/SPEC API table → removal is an API + spec decision. | **Done (B1 — user chose).** `ILocalizerFactory` is kept as native API (per D-H) but **`LocalizationContext` now *is* the factory** (explicit `Create<T>()`→`For<T>()`, `Create(cat)`→`ForCategory(cat)`). **Deleted `LocalizerFactory` + `CategoryLocalizer(<T>)`** (the buggy, unused impl) — the `EnsureCulture` gap dies by construction (`For<T>()` already triggers on-demand load). DI now **registers `ILocalizerFactory`** → the context (the missing D-H door). Redirected `CategoryLocalizerTests` + `LocalizerAllocationTests` onto `context.For<T>()`; the allocation test now guards the **real injected path** (`EnsureCulture` per-lookup **confirmed zero-alloc** in steady state). Dropped the one test that asserted the deleted factory's per-type caching (B1 doesn't promise it). Added a DI test locking in the injectable factory door. Full build + all 7 suites green. |
| LOC-3 | 🟡 | EnumLocalizationExtensions.Resolve:54 | Recomputes the category inline as `type.FullName ?? type.Name` instead of `CategoryName.Of(type)` (the one owner of "category for a scope type"). Same result for an enum (never generic), but it duplicates the ownership and could drift. | **Done** — calls `Internal.CategoryName.Of(type)`. Build green; 135 core + 12 e2e pass. |
| LOC-4 | 🟠 | Providers: Directory/Resource/Manifest `CultureFrom*` | **One naming convention, three owners.** `DirectoryCatalogProvider.CultureFromFileName`, `ResourceCatalogProvider.CultureFromName`, and `ManifestCatalogProvider.CultureFromUri` each re-implement the `{name}.{culture}.{ext}` culture-suffix parse (the comments admit "the same rule the directory provider uses"). Manifest's version additionally strips a URI query/fragment. R4 duplication. | **Done** — extracted `Providers/CatalogFileName.CultureOf(nameOrUri)` (query-stripping folded in, so it subsumes all three); deleted the three copies. Behavior-preserving. Build green; 135 core + 12 e2e pass. |
| LOC-5 | 🟡 | CatalogLoader.SplitComposite + Formats/PoTranslationFormat.PoEntry.ToCatalogEntry | **Composite-key *decompose* has two owners.** `TranslationKey.Compose(key, context)` → `context⁴key` has one owner, but the inverse (split at the first `Separator` → `(key, context)`) is hand-inlined in **two** places (the flatten loader and the PO reader), identically. The owner of the convention owns only one direction. | **Done** (user approved). Added `TranslationKey.Decompose(composite) → (string Key, string? Context)` in Abstractions — the inverse of `Compose`, one owner for both directions (netstandard2.0 slice via the repo's `#if` pattern). Replaced `CatalogLoader.SplitComposite` and `PoEntry.ToCatalogEntry`'s inline splits. Behavior-preserving. Build green; 135 core + 17 abstractions + 59 tooling + 12 e2e pass. |
| CS-1 | 🟠 | CatalogStore ↔ DefaultLocalizer / LocalizationContext | **Misplaced responsibility (user agreed).** The catalog store owned the `RenderingContext` (source culture + missing-arg policy + `MessageFormatter`) — a *rendering* concern — purely as the "reconfigure sink" `DefaultLocalizer` read live. A catalog loader shouldn't own the formatter. | **Done.** Moved the `RenderingContext` onto `DefaultLocalizer` (a `volatile` field, `Reconfigure`/`SourceCultureName` internals). `LocalizationContext` is now the reconfigure coordinator: `Configure`/`Reset` re-derive the rendering context and push it to the engine, then configure the store. `CatalogStore` no longer references `RenderingContext` at all (and no longer reads `SourceCulture`/`MissingArguments` from options). Threaded through the 3 direct `DefaultLocalizer` constructions (context + 2 test/bench helpers). Behavior-preserving (live reconfigure + missing-arg-policy tests green); full build + all suites pass. |
| CS-2 | 🟡 | Providers/CatalogDescriptor | **Stale docs (a doc lie, not dead code).** `CatalogDescriptor.Format`'s doc said the **store** uses it "to pick a parser," and the class doc said "the store parses the stream" — both false since the *provider owns the parse* (`Source` yields a ready `Catalog`); `Format` is only a provider-side precedence/diagnostics tag. `Identity` said "the store dedupes" (now the loader). | **Done.** Corrected the class doc, `Format`, and `Identity` docs to match the provider-owns-the-parse / loader-dedupes model. Also fixed 3 stale `CatalogStore` comments (they said "re-derives the context" (moved in CS-1) and "per-provider state" (`ProviderState` gone)). The field itself is used (directory precedence) — doc-only fix. |
| CS-3 | 🟠 | CatalogStore ↔ CatalogLoader | **God-class breadth — resolved by giving the loader ownership of loading + dedup (user's insight).** The store was doing load orchestration, the async coalescing queue (#4), dedup, and snapshot batching (#6). **Done, in stages:** (1) renamed the misnamed `CatalogLoader`(Flatten) → `CatalogFlattener` to free the name; (2) extracted a real `CatalogLoader` that owns opening **sync + async** + coalescing; (3) moved the **loaded-catalog registry + dedup** into the loader — the store's `Commit`/`AlreadyHandled` **deleted**, `ProviderState` slimmed to `{Provider, Watch}`, the loader does the 2-line check+register the user asked for; (4) **collapsed the batching** — the store gathers a whole operation's work into one `loader.Load` and publishes once, so `BeginBatch`/`EndBatch`/`_batchDepth` are **deleted**. What remains of #6 is the irreducible publish (`Rebuild` = build snapshot from the loader → atomic swap → `CatalogsChanged`, publish-if-changed + baseline). Async miss-path still publishes per landing (inherent); awaited paths defer to one post-drain publish (no-flash preserved). Full build 0 warnings; all suites green (incl. async/hot-reload/reconfigure/failure coverage in `CatalogStoreTests`). "Commit" is gone — there was no commit, just the loader remembering what it loaded. **Later passes (user-driven):** (5) **deleted `ProviderState` and `ProviderCatalogs`** — the loader is now **lock-free**, a per-provider `ConcurrentDictionary<identity, Catalog?>` (null = failed) keyed by `ICatalogProvider`; the store holds `_providers` + a flat `_watches` bag. (6) publish path simplified to `Rebuild(bool changed)` — dropped `_dirty`/`MarkDirty`/`PublishLanded` (the async-await bridge is now a captured local); dropped single-use `DrainAsync`/`WorkForLoadedCultures`. (7) unified the loader's two load signals into a single `Action onChanged` (fired only on real growth, sync or async). (8) `WorkForChain` CQS split — the `_loadedCultures` marking lifted into an explicit `MarkChainInUse` command, leaving `WorkForChain` a pure query. |
| CS-4 | 🔴 | CatalogStore ↔ LocalizerOptions.Cultures | **Documented option that did nothing (a trap).** `LocalizerOptions.Cultures` is documented as "the target cultures to load; `null` discovers every culture," and `SourceCulture` references "the `Cultures` allow-list" — but `options.Cultures` was **read nowhere**. Setting `Cultures = ["de"]` still loaded every culture. | **Done — implemented.** Added `CatalogStore.IsCultureLoadable(culture)` (null list ⇒ all; else listed cultures + the always-loaded source culture + culture-neutral base catalogs) and applied it in both work builders (`WorkForInventory` per-descriptor, `AddDescriptors` per-culture) so it holds for eager, on-demand, and preload. Test `Cultures_AllowList_LoadsOnlyListedCulturesAndTheSource`. The option's docs are now accurate rather than aspirational. |
| CS-5 | 🔴 | CatalogLoader.LoadedCatalogs | **Regression I introduced in CS-3, caught by the suite.** Moving the registry to a per-provider `ConcurrentDictionary` made `.Values` **unordered**, so two overlapping same-culture catalogs from one provider merged in a random order → `Directory_TwoSameFormatFilesOverlap_ResolveADeterministicWinner` flaked (the old plain `Dictionary` had preserved insertion order). | **Done.** `LoadedCatalogs` now orders each provider's catalogs by identity **(ordinal `Culture` then `Name`)** before the merge, so the later ordinal name wins deterministically — independent of the registry's layout **and** of the file system's enumeration order (making the test's aspirational comment actually true). Ran the test 5× + the core suite 3× — stable. |
| LOC-6 | 🟠 | Formats/IcuPluralScanner | **A third ICU scanner.** `IcuPluralScanner` re-implements the grammar's brace/whitespace/apostrophe-quote scanning (`SkipQuote` with `{`/`}`/`#`, depth counting, `ReadIdentifier`) that `MessageGrammarParser` already owns — the same R4 smell as MF-5 (`OtherBranchInserter`), now across assemblies. It exists because it needs each plural branch's **raw body text** (for gettext `msgstr[n]`), which the position-free `Message` AST discards. | **Done — option (a).** Added a `Message`→ICU serializer (`MessageWriter`, re-quotes syntax so it round-trips) and a public recognize/build pair on `MessageSyntax` (`RecognizeCardinalPlural` → parses with the real parser and re-emits each branch; `BuildCardinalPlural` the inverse; `CardinalPlural` shape). `PoPluralConverter` now calls these — no grammar re-scan; **`IcuPluralScanner.cs` deleted** (scanner + `IcuPluralShape` + `PluralCategoryKeyword`). MessageFormat owns all ICU recognition/emission; core does only gettext-order mapping. New `MessageSyntax` tests (simple recognize, 6 rejection cases, quoted-brace round-trip); the exact PO plural round-trip test still passes. 115 MF + 135 core green, 0 warnings. |

### DependencyInjection
**Files:** ServiceCollectionExtensions · InjectedLocalizer (was AmbientLocalizer)

Reviewed R1–R8 against D-H (native API = `ILocalizer`/`ILocalizer<T>`/`ILocalizerFactory`) and D-I (DI feeds the one ambient store, an escape hatch — *not* a parallel system). The D-I parts are correct and intentional: `AddArchPillarLocalization` configures + registers `Localizer.Ambient`, and the `ILocalizer`/`DefaultLocalizer` views + idempotency guard are right. The gaps were the missing `ILocalizerFactory` door and the bridge naming — both resolved via **LOC-1** (rename) and **LOC-2/B1** (context-as-factory, registered). No separate findings; all folded into LOC-1/LOC-2. Full suite green.

### AspNetCore
**Files:** ArchPillarDataAnnotationsLocalizer · DataAnnotationsLocalizationMvcBuilderExtensions · TranslationStaticFileExtensions

Reviewed R1–R8. Clean, small adapter (238 LOC): the `IStringLocalizer`→`ILocalizer` DataAnnotations bridge (text-as-key + `[Localized…]` twin defaults, positional→named ICU args), the MVC wiring, and the static-file content-type registration. Packaging correct (R8: net8/9/10, `FrameworkReference Microsoft.AspNetCore.App`). Notes (no action): `GetAllStrings` returns `[]` (fine — MVC doesn't enumerate it); the DataAnnotations seam resolves through the ambient `Localizer` (deliberate per D-I). Format instances are created just to read their `.Extensions` (harmless static init).

| ID | Sev | Where | What we found | How we fixed it |
|----|-----|-------|---------------|-----------------|
| ASP-1 | 🟡 | DataAnnotationsLocalizationMvcBuilderExtensions.CategoryOf | **Generic model type would mis-derive its category.** `CategoryOf` was `type.FullName ?? type.Name`, but the extractor + runtime `CategoryName.Of` file a generic scope type under its **open-generic** name (`Foo\`1`); a generic model's extract-time and lookup-time categories would disagree → no translation resolves. Vanishingly rare in practice. | **Done** — folded into SL-1: deleted the inline helper and now calls the newly-public `CategoryName.Of(type)` (one owner). Build green; 12 tests pass. |

### WebAssembly
**Files:** WebAssemblyHostLocalizationExtensions

Reviewed R1–R8. **Clean — no findings.** One method (49 LOC): fetch the build-emitted manifest via `ManifestCatalogProvider.CreateAsync` (using the app's DI `HttpClient` + the configured source culture/formats), layer it above `options.Providers`, reconfigure the ambient store, and `await LoadCultureAsync(CurrentUICulture)` so the first render is localized (the no-flash awaited path). Consistent with D-I (feeds the ambient); `_ => provider` is the documented already-built-provider factory. No test project — the WASM-host glue can't be unit-tested without a browser host, and the `ManifestCatalogProvider` it wraps is covered in core.

### Tooling
**Files:** Program · ToolApplication · Reconciler · Internal/AssemblyStringExtractor · Internal/TemplateBuilder ·
Internal/CatalogDirectoryResolver · Internal/ScopeDiscovery · Internal/ScopeResolver
**Now also:** Commands/{Status,Extract,Add,Sync,Convert,Export,Import,Merge,Manifest}Command · Commands/ScopeSettings · Internal/{ToolConsole,CatalogNaming,CatalogIo}

_Reviewed R1–R8. TOOL-1..5 fixed. `Reconciler`, `CatalogDirectoryResolver`, `ScopeDiscovery`, `ScopeResolver` are clean (no findings) — dense but each with one job and good docs._

| ID | Sev | Where | What we found | How we fixed it |
|----|-----|-------|---------------|-----------------|
| TOOL-1 | 🟠 | ToolApplication (was 843 LOC) | **Spaghetti — all 9 commands jammed into one static class**, plus a two-owners smell: each command's allowed options lived in a `_knownOptions` dictionary *separate* from its method, alongside hand-rolled arg parsing, dispatch `switch`, and usage text. | **Done — adopted `Spectre.Console.Cli`** (user's call: a tool-only dep, never in a consumer's product). Each command is now a self-contained `AsyncCommand<Settings>` in its own file (name + typed `[CommandOption]`s + logic in one place); the `_knownOptions` dict, `ParseOptions`/`Require`, dispatch switch, and `Usage` are **deleted** — `ToolApplication` is a ~65-line `CommandApp` shell. Shared helpers extracted to `CatalogNaming`/`CatalogIo`/`ToolConsole` (+ two `ScopeSettings` bases). Behavior preserved: exit codes (0/1/2), file effects, stderr `error:`/`warning:`/`drift:` substrings, and — via `config.UseStrictParsing()` — the "a typoed option can't silently turn a check into a write" safety. **Professional output** (user request): `Status()` spinners on the slow assembly/zip ops, a `Table` for `status`, green `✓` success + colored auto-generated help; errors/warnings stay plain on stderr. Also dropped the now-pointless `ConfigureAwait(false)` (console exe has no `SynchronizationContext`; CA2007/RCS1090 are both `none`). 59 Tooling + 12 EndToEnd tests green; 0 warnings. |
| TOOL-2 | 🟡 | AssemblyStringExtractor (now AnnotationExtractor.AddConcept) | **Doubled, self-contradicting comment** — two "Emits one site for a display concept." paragraphs, the first wrongly calling the `[Localized…]` twin the key, the second (correct) making the system attribute's value the key and the twin the default. | **Done** — collapsed to the single correct paragraph (while moving it in TOOL-4). |
| TOOL-3 | 🟢 | AssemblyStringExtractor ↔ TemplateBuilder | **Same module parsed twice per assembly** — `Extract` and `ExtractAnnotations` each `ReadModule(path)`, and `TemplateBuilder.Build` always called both, so a solution scan parsed every assembly's IL metadata twice. The two-method split existed for "opt out of annotations," but the only caller passes a bool. | **Done** — folded into TOOL-4: the coordinator reads the module **once** and runs both passes over it; `Build` now takes the `(CallSites, Annotations)` tuple and the `includeAnnotations` bool. |
| TOOL-4 | 🟡 | AssemblyStringExtractor (605 LOC) | **One class, two extraction algorithms** — the IL evaluation-stack simulation and the attribute/annotation reading, sharing only the resolver + `RawCallSite` (the `_bindings` cache serves only the IL path). | **Done** — split into `CallSiteExtractor` (IL sim, owns `_bindings`), `AnnotationExtractor` (stateless → `static`), and `AssemblyModuleReader` (the shared Cecil resolver + `AllTypes`). `AssemblyStringExtractor` is now a ~30-line coordinator that reads once and delegates; `RawCallSite` moved to its own file. |
| TOOL-5 | 🟢 | TemplateBuilder | **Re-rolled identity composition** — dedup key `Category + "\0" + Key + "\0" + Context` duplicated the "(category,key,context)→identity" that `Reconciler` gets from `TranslationKey.ComposeQualified`. | **Done** — `TemplateBuilder` now uses `TranslationKey.ComposeQualified` too, so the template dedup and the reconciler index agree on identity by construction (one owner). |

### StringLocalizer
**Files:** LocalizerStringLocalizer · LocalizerStringLocalizerFactory · StringLocalizerServiceCollectionExtensions · StringLocalizerMarker

Reviewed R1–R8. Careful, well-documented interop (254 LOC): the composing `IStringLocalizer` adapter (ambient override → inner `.resx` factory → verbatim name; overrides not run through ICU on a miss), the factory (`CategoryName.Of`), the marker for idempotency, and the DI extension whose `ResolveInnerFactory` handles keyed/instance/factory/type descriptors + the missing-`ILoggerFactory` degrade (citing D-J/D-F3). The complexity is inherent to composing over an existing factory registered before or after. No internal findings.

| ID | Sev | Where | What we found | How we fixed it |
|----|-----|-------|---------------|-----------------|
| SL-1 | 🟡 | LocalizerStringLocalizerFactory + AspNetCore.CategoryOf + EnumLocalizationExtensions | **Category derivation had 3 owners + an `InternalsVisibleTo` inconsistency.** `CategoryName.Of` (core, `internal`) was reachable by StringLocalizer/DI (they have IVT) but not the `AspNetCore` **main** project (only `AspNetCore.Tests` had IVT), forcing AspNetCore to reimplement it (ASP-1). **Decision: #3 (user)** — make the helper public so any adapter/consumer derives extractor-compatible categories from one place. | **Done.** Moved `CategoryName` out of `Internal` → **public** `ArchPillar.Extensions.Localization.CategoryName.Of(Type)` (XML-documented, null-guarded). AspNetCore now calls it (inline helper deleted); StringLocalizer/EnumLocalizationExtensions updated; StringLocalizer's now-unused `Internal` using dropped. Full build + all suites green. |

---

## Open questions / decisions

### Money / number-formatting notes (surfaced during review)
- **Currency display already works:** `{x, number, currency}` → .NET `"C"` → "$19.99" / "$5.00" (forced
  decimals per culture). Covers the user's price / PO display need today. *(Caveat: appears untested — worth a
  test to lock it in.)*
- **Gap — explicit currency code (user-confirmed requirement):** `"C"` uses the *rendering culture's*
  currency, so a USD price under a German UI renders "€". The currency must be **specified explicitly** and
  must *not* change with the language selector. Plain `"C"` can't do this — needs currency-code-aware
  formatting (ICU skeletons do `currency/USD`).
- **Gap — symbol-less fixed decimals:** only `integer`/`percent`/`currency` styles exist; no "N fraction
  digits" (e.g. "19.99" with no symbol).
- **MF-8** (plural `#` display vs selection digits) and **MF-6** (`minFractionDigits` plural-operand hook) are
  about *plural operands* — orthogonal to the display gaps above.
These are net-new feature work, separate from the review — schedule as needed.

### Deferred to later steps (forward items)
- **`features.md` (4×) + skill ref `di-runtime-and-interop.md`** still describe `ITranslationSource` as a live layering/extension point. Its replacement is the runtime provider model (`ICatalogProvider`), which lives in core — reconcile these during the **core Providers** review (Tier 2). [from ABS-1]
- **Numbered spec `03-container-formats.md`** had broader drift than the line fixed (it predated the sync-`Read` and `CatalogWriteOptions` changes; now corrected for those). The numbered specs are explicitly allowed to lag (DECISIONS.md overrides), so a fuller `00`–`06` reconciliation is a separate docs pass, not part of this code review. [from ABS-4]

---

## Cycle 2 — independent multi-agent review pass

A second, adversarial pass: **80 Sonnet-5 agents** (one per matrix cell, R1–R8 × 10 assemblies) reviewed the *current* code fresh — including the ~1000 lines written during cycle 1 (Spectre CLI, extractor split, public `CategoryName`) that had never had an independent review. Then **78 verifier agents** (one per finding, prompted to refute; docs treated as known-stale/deferred) collapsed the raw findings.

**Funnel:** 78 raw → verified (71 confirmed / 6 partial / 1 refuted) → **52 fix / 23 defer-doc / 3 wontfix**.

**Applied — 47 code fixes across all 10 assemblies, all suites green, 0 warnings.** Highlights (real bugs, many in cycle-1/new code):
- **Core `CatalogLoader` `onChanged` race** (my refactor): a coalesced concurrent fetch left a second awaiter's snapshot stale → force-rebuild after drain.
- **Core `DirectoryCatalogProvider`**: the "highest-precedence wins" rule was implemented twice and had drifted (recursive vs top-dir); `DescribeWinner` now reuses `Discover`.
- **Tooling `Reconciler`**: sync overwrote `References` from the always-empty IL template → **silent data-loss** of hand-added refs; now preserved when the template has none.
- **MessageFormat**: NaN/∞ plural arg → `OverflowException` (now proper exception); duplicate selectors now rejected.
- **Analyzers**: missed `?.["key"]` (`ElementBindingExpression`); non-constant `[TranslationComment]` now reported.
- **DI**: raw `DefaultLocalizer` engine registered injectable = a door skipping on-demand load → removed.
- **AspNetCore**: `DisplayKey`/`DescriptionKey` inverted MVC's `[Display]`-before-`[DisplayName]` precedence.
- **StringLocalizer**: unguarded inner-factory exception; non-generic `IStringLocalizer` hardcoded `inner:null` (didn't compose) → routed through the composing factory.
- Plus dead-code deletions (public `Resolve`/`Formats`, `Detect`-API internalized, `SuppliedArguments`, `PluralOperands.C`) and one-owner dedups (`CultureChain`, `PluralRanges`/`ContainsOther`, shared diagnostic ids, `ScopeRunner`/`SyncTargetAsync`/`ScopeSettings` base, `CatalogFileName.ExtensionOf`).

**Declined (engineering judgment over the verifier's "fix"):**
- AspNetCore twin-rule vs `EnumLocalizationExtensions` — different member kinds (enum vs model); sharing = over-abstraction.
- StringLocalizer scoped/transient inner factory collapse — inherent captive-dependency; `IStringLocalizerFactory` is a singleton contract.
- StringLocalizer positional→named args dup — 8 trivial lines across two adapter packages; public/IVT helper disproportionate.

**TOOL-C2-11 — resolved (user's directory model).** `extract`/`add`/`sync` now write **per-project** (each assembly → the `--output` subfolder, default `Translations`, of its own project — found by walking the built `.dll` up to its `.csproj`; a loose `--input`/`--assembly` writes beside the input base). `--output` is smart: **absolute** used as-is, **relative** resolved against the project/input base (via `Path.Combine`), never one shared flat folder. This matches how `export`/`merge`/`manifest` read (per-project), closing the mismatch; the documented multi-project workflow now round-trips. `merge`/`manifest` kept as-is (names + all), pending docs. New `CatalogDirectoryResolverTests`; Tooling 61 + EndToEnd 12 green.
  - _Import follow-up — done._ `import` now routes each returned catalog back to its **own project** (by the assembly name in the entry → `ProjectDirectoriesByName`), matching where the authoring commands wrote it; an absolute `--output` still forces one flat dir, a relative one resolves per project (lazily, so absolute `--output` needs no scope discovery). Removed the now-unused `ResolveWriteDirectory`. New `SolutionScope_Import_DistributesEachCatalogToItsOwnProject` test; EndToEnd 13 green.

### Gaps & docs — status (post-cycle-2)

The confirmed **bug/design fixes are complete** (47 cycle-2 fixes + TOOL-C2-11 + import). What remains, per the "fix bugs → gaps → docs" plan:

**Gaps (deferred *features*, need a want/YAGNI triage before building):**
- ✅ **LOC-6** — done (see the Localization-core findings table): the third ICU scanner deleted; `MessageSyntax` now owns recognize/build + a `Message`→ICU serializer.
- ⬜ **MF-6 / MF-8** — plural-selection vs `#`-display fraction-digit disagreement + the untested `currency` render path (see the MessageFormat findings table).
- ⬜ **Money / number-formatting** — see _Money / number-formatting notes_ below.
- ⬜ **Tooling PDB-based `References`** — populate `CatalogEntry.References` from PDB sequence points (the IL extractor currently can't; the data-loss half was already fixed in `Reconciler`).

**Docs — not started (the big reconciliation):** the **23 cycle-2 defer-doc** items + the older drift (from ABS-1 etc.). Notably `SPEC.md`/`05-runtime.md`/the numbered specs describe gone or absent APIs (`AddCatalog`/`AddSource`/`Reload`/`AddProvider`), and nothing documents the Spectre CLI surface, the per-project directory model settled in TOOL-C2-11, or corrects the DataAnnotations non-goal.

**Decisions the user has settled:** interpolated-string handlers — **not** implementing (ICU incompatibility, breaks constant-default extraction, no short-circuit benefit, second door); `merge`/`manifest` — keep names + all, just document.
