# 03 — Container Formats (Provider Abstraction + Portable Object + XLIFF 2.1 + Application Resource Bundle)

> Assemblies: `ArchPillar.Localization.Abstractions` (the `Catalog` model + provider interface), `ArchPillar.Localization.Formats.Po`, `.Formats.Xliff`, `.Formats.Arb`. Providers reference `MessageFormat` (spec 04) only for plural conversion (Portable Object) and validation.

## Purpose

One interface that the reconciler (spec 02) and the runtime (spec 05) use to read and write translation files, so neither knows or cares which on-disk format is in play. Three implementations: Portable Object for simple/community handoff, the Extensible Markup Language Localization Interchange File Format (XLIFF) version 2.1 for professional handoff, and the Application Resource Bundle (ARB) as the JavaScript Object Notation answer (spec-backed and ICU-native). Generic untyped JavaScript Object Notation is deliberately not implemented — it has no standard schema and therefore no interoperable translator tooling.

## The canonical model (in `Abstractions`)

The `Catalog` and `CatalogEntry` from spec 02 are the single in-memory representation. Providers convert to and from it. The reconciler and runtime operate only on this model.

```csharp
public sealed record Catalog
{
    public required string Culture { get; init; }      // BCP-47; source-locale catalog uses the source language
    public required IReadOnlyList<CatalogEntry> Entries { get; init; }
    public IReadOnlyDictionary<string, string> Headers { get; init; }
        = new Dictionary<string, string>();             // format-specific extras round-tripped opaquely
}
```

## Provider interface

```csharp
public interface ITranslationFormat
{
    string FormatId { get; }                 // "po" | "xliff" | "arb"
    IReadOnlyCollection<string> Extensions { get; }   // e.g. [".po"], [".xliff",".xlf"], [".arb"]
    FormatCapabilities Capabilities { get; }

    Task<Catalog> ReadAsync(Stream input, CancellationToken ct);
    Task WriteAsync(Stream output, Catalog catalog, CancellationToken ct);
}

[Flags]
public enum FormatCapabilities
{
    None              = 0,
    Context           = 1 << 0,  // disambiguation context distinct from key
    Comments          = 1 << 1,  // translator comments
    SourceReferences  = 1 << 2,  // file:line where used
    ExplicitState     = 1 << 3,  // a translation-state field (vs. inferred)
    NativePlural      = 1 << 4,  // plurals expressed in the format's own scheme
    IcuPlural         = 1 << 5,  // plurals expressed as ICU MessageFormat in the value
    PreviousSource    = 1 << 6   // can record the prior source text on drift
}
```

The reconciler reads `Capabilities` to decide how to represent `TranslationState` and previous-source on each format. Removed keys are deleted from the file (Decision D-11), so no provider writes obsolete markers and there is no obsolete capability. The runtime ignores capabilities and just reads entries.

### Capability matrix

| Capability | Portable Object | XLIFF 2.1 | Application Resource Bundle |
|---|---|---|---|
| Context | yes (`msgctxt`) | yes (group/unit names, notes) | yes (`@@context`, per-key metadata) |
| Comments | yes (`#.` extracted, `#` translator) | yes (`<note>`) | yes (`description` in `@key` metadata) |
| Source references | yes (`#:`) | yes (`<note>` / unit metadata) | partial (custom `x-` attribute) |
| Explicit state | inferred (`fuzzy` + empty `msgstr`) | **native** (`segment state`) | custom metadata attribute |
| Plurals | **native** (gettext scheme) | ICU verbatim in value | **ICU verbatim** in value |
| Previous source | yes (`#\| msgid`) | `<note>` annotation | custom metadata attribute |

The asymmetry that drives complexity: Portable Object has the richest *metadata* affordances (every reconciler concept maps to a native comment marker) but the weakest *plural* model (its own integer-indexed scheme rather than named ICU categories). XLIFF and ARB are the reverse on plurals (ICU-native) but lack a native translation-state (XLIFF has one; ARB does not).

## Portable Object provider (`Formats.Po`)

`Capabilities = Context | Comments | SourceReferences | NativePlural | PreviousSource`.

- **Parser:** hand-rolled line-oriented reader. Handle `msgid`, `msgstr`, `msgctxt`, `msgid_plural`, `msgstr[n]`, multi-line continuation (adjacent quoted strings), C-style escapes, comment markers `#`, `#.`, `#:`, `#,` (flags including `fuzzy`), `#~` (obsolete), `#| msgid` (previous source). Parse the header entry (empty `msgid`) into `Catalog.Headers`, including `Plural-Forms`. **On read, discard any `#~` obsolete entries** (a file hand-edited in another tool may contain them; we do not carry them forward). **On write, never emit `#~`** — removed keys are simply absent (Decision D-11).
- **Key model:** our key is symbolic, but Portable Object's identity is `(msgctxt, msgid)`. **Store our `Key` in `msgctxt`** when no semantic context exists, or as `msgctxt = "<context>\u0004<key>"`-style composite when both are needed — define one convention and keep it. Put the **source default** in `msgid` so Poedit shows readable source text. This preserves Poedit's expectation that `msgid` is the source string while keeping our stable key addressable. *(Document this convention prominently; it is the one place the Portable Object mapping is non-obvious.)*
- **Header on write / create:** when writing a file — especially a newly created target file (tool `add`, spec 02) — generate the header entry including a correct `Plural-Forms` for the locale, derived from the embedded CLDR data via `PluralRules.GettextOrder` (spec 04). This is why creating a language is the tool's job rather than something Poedit guesses for non-Portable-Object formats: only the source side reliably has the key set *and* the plural rules to produce a correct header for an arbitrary language.
- **State mapping:** `NeedsReview → fuzzy` flag; `NeedsTranslation → empty msgstr`; `Translated/Final → present msgstr, no fuzzy`. (Portable Object cannot distinguish Translated from Final; both serialize the same. On read, a non-empty non-fuzzy `msgstr` maps to `Translated`.)
- **Plural conversion:** this is the provider's hard part. On **write**, convert the ICU `plural` construct in `SourceMessage`/`TranslatedMessage` into gettext `msgid_plural` + `msgstr[n]` using the locale's `Plural-Forms` ordering (derived from the same CLDR data, spec 04, so ICU category ↔ gettext index is consistent). On **read**, convert back to an ICU `plural` value. Messages with `select`/`selectordinal` or nested plurals that gettext cannot represent must be kept as ICU text in `msgid` and flagged; do not silently lose them. Define the supported-conversion boundary explicitly and emit a warning at the boundary.
- **Compilation:** do **not** produce binary `.mo`; the runtime reads `.po` directly (spec 05). (A `.mo` writer can be a later addition if startup parsing cost ever matters.)

## XLIFF 2.1 provider (`Formats.Xliff`)

`Capabilities = Context | Comments | SourceReferences | ExplicitState | IcuPlural | PreviousSource`.

- **Parser/writer:** use `System.Xml` (`XmlReader`/`XmlWriter`) from the Base Class Library; do not hand-roll Extensible Markup Language tokenization. Respect namespaces and `xml:space`.
- **Structure:** `<xliff version="2.1" srcLang=... trgLang=...>` → `<file>` → one `<unit id="<key>">` per entry → `<segment state=...>` with `<source>` and `<target>`. Map our `Key` to `unit/@id` (it is the natural stable identifier — this is exactly what XLIFF `@id` is for, and the reason XLIFF fits a symbolic-key model better than Portable Object). Carry `Context`, `Comment`, `References`, and previous-source as `<note>` elements with a category attribute (`category="context|comment|reference|previous-source"`).
- **State mapping (native):** `NeedsTranslation → state="initial"`; `NeedsReview → state="needs-review-translation"`; `Translated → state="translated"`; `Final → state="final"`. This native state machine is the whole reason XLIFF is the professional choice — the reconciler's drift state is the standard's own state, no custom flag.
- **Value:** store ICU MessageFormat verbatim inside `<source>`/`<target>`. Do not attempt XLIFF's own inline-element plural representation; keep messages opaque so one rendering engine (spec 04) handles all formats. Preserve inline markup that a tool may have added on round-trip (read into the entry as opaque, write back unchanged) rather than discarding it.
- **Removed keys:** a deleted key's `<unit>` is simply absent on the next write (Decision D-11); there is no obsolete group.
- **Scope note:** target the localization-relevant subset of XLIFF 2.1 (units, segments, notes, state). Full XLIFF supports modules (translation candidates, glossary, validation) that are out of scope; round-trip unknown elements opaquely where feasible rather than dropping them.

## Application Resource Bundle provider (`Formats.Arb`)

`Capabilities = Context | Comments | IcuPlural` plus custom-attribute-backed `ExplicitState | SourceReferences | PreviousSource` (see below).

- **Format:** a single JavaScript Object Notation object per locale. Non-`@`-prefixed keys are translatable entries whose value is an ICU MessageFormat string. Each entry `"key"` has an optional metadata sibling `"@key"` carrying `description` (our `Comment`), `context`, and `placeholders`. File-level metadata uses `@@`-prefixed keys: `@@locale` (our `Culture`), and `@@last_modified`. Use `System.Text.Json` from the Base Class Library.
- **Key model:** the JSON member is the **category-qualified identity** (`QualifiedKey`), because ARB's flat object holds one member per entry and a key alone is not unique across categories. A global (uncategorized) entry is its **bare key** (`"home.greeting"`) — standard ARB, what translation tools pair on; a categorized entry is `"{category}::{key}"` (`"Acme.Greeter::greeting"`) with the category also in `"@key"`'s `x-category`; a global key beginning with `@` is escaped `"::@key"` so it is never mistaken for metadata. `Unqualify` strips the prefix using the entry's known category, so it round-trips. Source text is the value in the source-locale `.arb`; translations are the values in per-locale `.arb` files.
- **Value:** ICU MessageFormat verbatim. ARB is ICU-native, so plural/select round-trip with no conversion. `placeholders` metadata is informational; the runtime derives placeholder usage from the message itself, but writing the `placeholders` block improves the translator's view in Poedit and Translation Management Systems.
- **State, references, previous-source:** ARB defines no native fields for these. Persist them under custom `x-`-prefixed metadata inside `"@key"` (e.g., `"x-state"`, `"x-references"`, `"x-previous-source"`, `"x-source-fingerprint"`), which the spec permits and tools ignore. The fingerprint (`x-source-fingerprint`) must be persisted because the reconciler needs it and ARB has nowhere else to put it.
- **Removed keys:** a deleted key's entry and its `"@key"` metadata are simply absent on the next write (Decision D-11). ARB has no obsolete concept and we want none.

## Shared provider requirements

- **Round-trip fidelity:** `Read` then `Write` of an unchanged catalog must be byte-stable (modulo a single normalized formatting). This is what keeps version-control diffs meaningful and is a hard test gate.
- **Encoding:** UTF-8 without byte-order-mark, `\n` line endings, on every format.
- **Determinism:** entry ordering and attribute ordering are fixed by the writer, not by dictionary iteration order.
- **No partial writes:** write to a temporary file and atomically move into place, so a crash never truncates a translator's file.
- **Provider discovery:** providers are resolved by `FormatId`/extension through a small registry so the reconciler and runtime select one without hard references to the concrete assemblies (keeps formats genuinely pluggable; a consumer can ship only the providers they use).

## Acceptance criteria

- [ ] A catalog written by any provider and read back yields an equal `Catalog` (entry-by-entry), including state, context, comments, references, and fingerprint (within each format's capabilities).
- [ ] Read→Write of a file authored by the respective external tool (a Poedit `.po`, a real XLIFF 2.1 file, a Flutter `.arb`) preserves the file's meaningful content and does not corrupt tool-specific structure.
- [ ] A message containing `{count, plural, one {...} other {...}}` round-trips through XLIFF and ARB unchanged, and through Portable Object as a correct `msgid_plural`/`msgstr[n]` pair for the locale's plural form count, converting back to an equivalent ICU plural.
- [ ] A `select`/nested-plural message that gettext cannot represent is preserved (not lost) by the Portable Object provider and flagged.
- [ ] `TranslationState` round-trips natively through XLIFF, via `fuzzy`+`msgstr` inference through Portable Object, and via `x-state` through ARB.
- [ ] Writing is atomic (interrupting a write leaves the prior file intact).
- [ ] The provider registry returns the correct provider by extension and by `FormatId`, and an application that references only one Formats assembly works with that format and is unaware of the others.
