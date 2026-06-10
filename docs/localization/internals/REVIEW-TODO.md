# Library review — fix backlog (Roslyn pipeline · DI · Tooling · packaging)

From the four-area review (2026-06). Scope: all REDs, all ORANGEs, key YELLOWs, each with a
regression test. Ordering is by blast radius. Decision: **the category (namespace) is a first-class
part of an entry's on-disk identity and is never dropped** — the serialized key is always category-qualified.

## Proposed wire format (category-qualified identity, used by ARB member / XLIFF unit id / PO msgctxt)
- `Qualify(category, key)` = `category + "::" + key`; global (empty category) = `"::" + key` (the empty
  namespace, shown explicitly). Reverse: split on the FIRST `::` → category (never contains `::`), key =
  remainder (may contain anything). Fully reversible for (category, key) from the string alone.
- Context stays an annotation (ARB `context` / XLIFF note / PO is part of msgctxt already); where it is part
  of identity, it is folded into the qualified key so (category, key, context) is unique on disk.
- Tool reconciler identity becomes `(category, composite(key, context))`.

## RED
- [x] P-R1  Generator never extracts IStringLocalizer indexer sites (predicate lacks ElementAccessExpression; whole-compilation Detect() unused). + relax DetectAt guard. — FIXED.
- [x] T-R2  Bare `dotnet apl` crashes (Spectre parses `[options]`); escape `[[options]]`, move Usage() into try. — FIXED: Spectre dropped, plain Console usage; `RunAsync([])` returns 1.
- [x] P-R2  Same key in two categories → duplicate ARB JSON members (qualify the member name). — FIXED via QualifiedKey.
- [x] T-R1  Reconciler drops Category (New() + Composite() ignore it) → tool-produced translations unreachable. — FIXED.
- [x] D-F1  IStringLocalizer adapter renders the name as ICU before consulting the inner factory → throws on `{0:C}`/`{{`. — FIXED: raw found-aware ambient lookup; inner consulted before the default.

## ORANGE — formats / template (category-qualification cluster)
- [x] P-O1  Same key, different context dropped from the template (dedup ignores context). — FIXED: dedup keys on (category, key, context).
- [x] P-O3  `@`-prefixed key corrupts the ARB template (no guard, unlike the runtime writer). — FIXED: qualified member (`::@weird`) is never mistaken for metadata.
- [x] T-O4  `merge` to ARB with same key in two categories → duplicate JSON keys. — FIXED: qualified member per category.
- [x] T-O1  Untranslated ARB keeps stale source after a template change (ARB read sets TranslatedMessage even when NeedsTranslation). — FIXED: read returns null translation for explicit NeedsTranslation.
- [x] T-O7  ARB drift writes the translation as x-previous-source, not the old source (same root as T-O1). — FIXED: reconciler skips previous-source when source==translation.
- [x] T-O2  PO/XLIFF re-flag fuzzy on every sync (placeholders never persisted, always "changed").
- [x] T-O3  PO drops translators' `# ` comments on every sync. — FIXED: added CatalogEntry.TranslatorComment, round-tripped in PO, preserved by reconciler. (non-fuzzy flags / `#~` obsolete preservation still TODO.)
- [ ] T-O6  `convert` silently lossy; FormatCapabilities consumed by nothing — warn per lost capability.

## ORANGE — generator / analyzer
- [x] P-O2  Generated key registry fails to compile (no `\n\r\t\0` escaping; identifier collisions with category class / `TranslationKeys`). — FIXED: control-char literal escaping; single shared top-level member set seeded with enclosing type; per-class const set seeded with class name.
- [ ] P-O4  Analyzer APL0006/0007 order-dependent + blind to cross-file duplicates in the IDE — move to CompilationEndAction.
- [x] P-O5  Invalid `ArchPillarLocalizationKeyPattern` regex → AD0001 disables all APL diagnostics; add try/catch + match timeout.
- [ ] P-O6  `Localized<TSelf>` args overload gets no APL0003/0004 (SuppliedArguments only handles params).
- [x] P-O7  Extension-method (and object-creation) receivers lose the [TranslationScope] category → extracted global, resolved per-T.
- [ ] P-O8  Documented roll-your-own forwarder hits hard APL0001; recognize a forwarder (param carries the attribute) or add an opt-out.
- [ ] P-O9  Publish merge swaps stale bundles when the tool fails (apl-merged never cleaned; swap not gated on exit code).

## ORANGE — DI / CLI safety
- [ ] D-F2  Registration-time mutation of process-global ambient state; AddSource not idempotent; double-registration / multi-host cross-pollution; `""` directory ignored.
- [ ] D-F3  AddLocalization() after AddArchPillarLocalization() silently drops all .resx (TryAdd no-ops, inner captured null).
- [ ] D-F4  Injected concrete Localizer reads a different store than the interfaces; MissingArguments/FormatPrecedence/hot-reload options ignored on ambient paths.
- [ ] D-F5  IStringLocalizer.GetAllStrings omits ambient entries.
- [x] T-O5  `merge --output == --input` clobbers translator files; refuse equal paths.
- [x] T-O8  All tool output (incl. errors) vanishes when stdout is redirected (Spectre no-TTY); write errors via Console.Error.
- [x] T-O9  `add` without <lang> creates a junk file with exit 0.

## YELLOW (key ones to fix; rest are TODO)
- [x] T-Y1  Typo'd `--check` makes CI write files + exit 0 — reject unknown options.
- [x] T-Y2  sync --check can't distinguish drift (1) from error — use distinct exit codes.
- [x] T-Y3  Malformed-file errors omit the file path.
- [ ] P-O2b "Registry must compile" property test over adversarial keys.
- [ ] D-F6/7/8  Inner factory built outside container disposal / no TryAdd guard / keyed-descriptor skips an earlier unkeyed factory.
- [ ] Misc yellows (incrementality comparers, write-if-changed extract, conditional element access `loc?[...]`, reproducible-build paths, copy-to-output glob flatten) — TODO.

## Packaging (pre-publish; breaking later)
- [ ] PK-1  Roslyn pin 4.14 vs SDK floor — DECISION: keep the modern Roslyn pin, do NOT lower it; **document a
       minimum SDK** instead (SDK ≠ TFM: net8.0 targets build fine on a new SDK). Floor ≈ .NET SDK 9.0.3xx /
       VS 17.14 / .NET 10 SDK (the line shipping Roslyn 4.14+). Add to getting-started / recommendations and
       optionally a build-time check. Old-SDK consumers: extraction is silently off — the doc makes it explicit.
- [ ] PK-2  Move `TranslationMarkers.L` from Abstractions to the runtime (inert in Abstractions).
- [ ] PK-3  Amend DECISIONS 10.3 vs D-I — ILocalizer stays in the runtime (record the decision).
- [ ] PK-4  Tooling accidentally net10.0-only — multi-target net8;9;10.
- [ ] PK-5  Pack MSBuild assets under build/ not buildTransitive/.
