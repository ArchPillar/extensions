# Branch Review — `claude/clever-wright-Yo1hk`

**Scope:** new `ArchPillar.Extensions.Localization` subsystem — 117 files, ~10,997 insertions, 0 deletions (diff vs `origin/main`).

**Summary:** The architecture is solid and mostly built at the right depth — one `TranslationSiteDetector` shared by analyzer and generator, a single `MessageParser`, shared `SourceReferenceText`, and CLDR plural data generated from `eng/cldr/*.json` with the version pinned. The bugs cluster in two areas: **plural handling** and the **error/contract surfaces** of the public APIs.

---

## Correctness — should fix before merge

### 1. PO export tells gettext to ignore every plural form but the first — **highest severity**
`src/Localization/Formats/PoTranslationFormat.cs:161`

```csharp
var expression = count == 2 ? "(n != 1)" : "0";
```

For any language with 3+ forms (Polish, Russian, Czech, Arabic…) the header becomes `nplurals=4; plural=0;`. The `msgstr[1..3]` are written correctly, but Poedit/msgfmt/libintl evaluate `plural=0` for *all* n, so few/many/other translations never display — e.g. n=5 Polish shows the `one` form. The CLDR data needed to derive the real `plural=` predicate is already embedded; this should be generated from it, not a two-case constant. *Silent mistranslation for the majority of inflected locales.*

### 2. `IStringLocalizer` indexer throws instead of honoring the never-throw contract
`src/Localization.DependencyInjection/LocalizerStringLocalizer.cs:24` → `src/Localization/Localizer.cs:123`

The indexer passes the *key name* as the ICU default message. If a key contains an ICU-special char (`{`, `}`, stray `'`) and no override exists, `MessageFormatter.Format` throws `MessageFormatException`. The contract requires returning `new LocalizedString(name, name, resourceNotFound: true)` and never throwing. Both indexer overloads are affected.

### 3. Plural rendering crashes on a non-numeric argument
`src/Localization.MessageFormat/Internal/MessageRenderer.cs:294`

`ToNumber` falls through to `Convert.ToDecimal(value, …)`; `{count, plural, …}` formatted with `("count","abc")` throws a raw `FormatException` out of `Format()` instead of a typed/catchable error.

### 4. PO read crashes on sparse `msgstr[n]` indices
`src/Localization/Formats/PoTranslationFormat.cs:355`

```csharp
var forms = new string[_plurals.Count];
foreach (KeyValuePair<int, string> form in _plurals)
{
    forms[form.Key] = form.Value;
}
```

A `.po` with only `msgstr[0]` and `msgstr[5]` → count 2, write to index 5 → `IndexOutOfRangeException`. `CatalogLoader` swallows it at runtime, but the format is public API and `Tooling`/`Reconciler` call it directly and crash.

### 5. ARB read throws on any non-string value
`src/Localization/Formats/ArbTranslationFormat.cs:89` (also `:106`, `:110`)

`property.Value.GetString()` is called unconditionally; a numeric/object value (`{"count": 5}`) throws `InvalidOperationException` rather than a clean format error.

### 6. Generated `TranslationKeys.g.cs` won't compile if a key contains a newline/control char
`src/Localization.Generator/TranslationKeyRegistryEmitter.cs:64`

`Escape` only handles `\` and `"`. The detector accepts any constant string as a key; a multi-line constant emits a raw newline inside a regular string literal → the consumer's whole build breaks. (`TranslationGenerator.Literal` shares the narrow escaping.)

### 7. `plural`/`select` with no `other` branch renders empty, silently
`src/Localization.MessageFormat/Internal/MessageRenderer.cs:157` (`?? EmptyMessage`) and `RenderSelect` (`:199-205`)

ICU requires `other`; `MessageGrammarParser.FindConstructsMissingOther` exists but is never called on the parse/format path, so `{count, plural, one{# item}}` with count=5 emits nothing instead of failing at parse/build time.

### 8. `select` ignores `MissingArgumentPolicy.Throw`
`src/Localization.MessageFormat/Internal/MessageRenderer.cs:195`

The `TryGetArgument` result is discarded; a missing select argument silently renders the `other` branch (or empty), even when the policy is `Throw`. The `plural` path (`:133-136`) honors the policy; `select` doesn't.

### 9. `ReadInteger` overflow escapes even `TryParse`
`src/Localization.MessageFormat/Internal/MessageGrammarParser.cs:325`

`int.Parse` on `offset:99999999999` or `=99999999999` throws `OverflowException`; `TryParse` only catches `MessageFormatException`, so it propagates instead of returning false.

### 10. Reconciler won't flag drift on a translated-but-`NeedsTranslation` entry
`src/Localization.Tooling/Reconciler.cs:59`

The review flag is gated on `current.State != NeedsTranslation`, but state (not the presence of `TranslatedMessage`) drives it. After a coarse PO import, an entry with a real translation left in `NeedsTranslation` survives source drift without being surfaced for review or re-translation.

---

## Design / depth (worth addressing, not all blocking)

- **Duplicate ICU parser.** `src/Localization/Formats/IcuPluralScanner.cs` is a second, partial ICU parser (~250 lines, its own quote/brace handling) plus a third copy of the category↔keyword table. Spec `docs/localization/04-message-format-and-plurals.md` declares the AST/`MessageParser` *public*, but they were made `internal`, which forced the PO converter to hand-roll this. Making the AST public and pattern-matching the parsed tree would delete the scanner and the drift risk.
- **Two different culture-fallback chains.** `Localizer.Resolve` walks `CultureInfo.Parent` (`Localizer.cs:167`); `PluralRules.CultureCandidates` splits on `-`. For `zh-Hant-TW` they resolve different bases, so lookup and plural selection can disagree. Define the chain once.
- **CLDR `e`/`c` operands pinned to 0** (`src/Localization.MessageFormat/PluralRules.cs:72`). The generated `many` rules for fr/es/pt/it (`… or e != 0..5`) have a permanently-dead branch and there's no API to supply `e`; the embedded rule set overstates coverage.
- **"Allocation-free" claim has a hole.** `TranslationKey.Compose` (`src/Localization.Abstractions/TranslationKey.cs:29`) allocates `context + Separator + key` on every lookup *with a context*; `LocalizerAllocationTests` only exercise `context: null`, so the regression is untested.
- **`CatalogLoader.TryRead` swallows all exceptions with no logging hook** (`src/Localization/Internal/CatalogLoader.cs`): a single malformed translation file silently disappears at startup with no diagnostic.
- **Minor:** triplicated `Internal/Polyfills.cs` (MessageFormat / Detection / Abstractions); ARB `@`-prefixed keys are written as top-level properties but read back as metadata (round-trip loss for `@`-keys); `extract` reads the baked ARB where `TranslatedMessage = SourceMessage`, so converting to PO/XLIFF emits the source as if already translated.

---

*Read-only review — no source files were modified.*
