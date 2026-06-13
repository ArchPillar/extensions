# 04 — Message Format & Plural Rules

> Assembly: `ArchPillar.Localization.MessageFormat`. No third-party dependencies. The Unicode Common Locale Data Repository (CLDR) plural-rule data is embedded as source generated at build time from a checked-in, version-pinned data file. This is the foundational assembly — build it first.

## Purpose

Parse, validate, and render the message value grammar used by every format and the runtime: International Components for Unicode (ICU) MessageFormat. Provide correct plural-category resolution for any CLDR language from embedded data, with no runtime package dependency, because .NET's `System.Globalization` does not expose CLDR plural categories.

## In scope

- An ICU MessageFormat parser producing an Abstract Syntax Tree.
- A validator (used by the analyzer spec 01, the extractor spec 02, and the formats spec 03).
- A formatter that renders the Abstract Syntax Tree against an argument set and a target culture.
- Embedded CLDR cardinal and ordinal plural rules, generated into source.
- Placeholder extraction (the set of argument names a message references).

## Out of scope

- The Portable Object ↔ gettext plural index conversion (that lives in the Portable Object provider, spec 03; it consumes the plural-category data exposed here).
- Locale-aware number/date formatting beyond delegating to `System.Globalization` (see "Formatters").

## Supported grammar subset

Implement the core ICU MessageFormat constructs:

- **Plain text** with escaping. An apostrophe quotes special characters per ICU rules (`'{'` is a literal brace; `''` is a literal apostrophe). Implement ICU quoting exactly — it is the most common source of subtle bugs.
- **Simple argument:** `{name}` — substitutes the argument's culture-formatted value.
- **Typed argument:** `{name, type}` and `{name, type, style}` for `number`, `date`, `time` (delegated to `System.Globalization`). Recognize `{name, type}` even when a style is omitted.
- **`plural`:** `{count, plural, offset:n? one {…} other {…} =0 {…}}` with explicit-value selectors (`=0`, `=1`, …), keyword categories (`zero one two few many other`), the `#` token (the formatted number, minus offset), and a required `other` branch. Support nested constructs inside branches.
- **`selectordinal`:** same shape as `plural`, resolved against **ordinal** plural rules.
- **`select`:** `{gender, select, male {…} female {…} other {…}}` with a required `other` branch; arbitrary string keys.
- **Nesting:** any branch body is itself a full message (arguments and sub-constructs allowed).

Explicitly out of scope for v1 (validate-and-reject or pass-through, but document): `choice` (deprecated in ICU), and ICU `number`/`date` skeleton syntax beyond named styles. Note ARB's constraint that Flutter does not support plural `offset`; the parser supports `offset` generally, and the Portable Object/ARB providers may warn when targeting a consumer that does not.

## Abstract Syntax Tree

```csharp
public abstract record MessagePart;
public sealed record LiteralPart(string Text) : MessagePart;
public sealed record ArgumentPart(string Name, string? Type, string? Style) : MessagePart;
public sealed record PoundPart : MessagePart; // '#' inside a plural branch
public sealed record PluralPart(
    string ArgumentName, bool Ordinal, int Offset,
    IReadOnlyDictionary<PluralSelector, Message> Branches) : MessagePart;
public sealed record SelectPart(
    string ArgumentName,
    IReadOnlyDictionary<string, Message> Branches) : MessagePart;

public sealed record Message(IReadOnlyList<MessagePart> Parts);

// A selector is either an explicit numeric value (=N) or a category keyword.
public readonly record struct PluralSelector(int? ExplicitValue, PluralCategory? Category);

public enum PluralCategory { Zero, One, Two, Few, Many, Other }
```

```csharp
public static class MessageParser
{
    public static Message Parse(string text);                 // throws MessageFormatException with position
    public static bool TryParse(string text, out Message? message, out MessageFormatError? error);
    public static IReadOnlyCollection<string> ExtractPlaceholders(Message message); // all referenced arg names
}
```

`ExtractPlaceholders` is what spec 01 uses to populate `TranslationSite.Placeholders` and what the analyzer uses for `APL0003`/`APL0004`. It must be the single source of "what arguments does this message use."

## Validation

A single `Validate(Message)` producing structured errors, reused everywhere:

- Every `plural`/`selectordinal`/`select` has an `other` branch (else the analyzer's `APL0005`).
- `#` appears only inside a `plural`/`selectordinal` branch.
- Branch category keywords are valid `PluralCategory` values.
- No duplicate selectors within one construct.
- Argument names are well-formed identifiers.

The parser reports errors with character offsets so the analyzer can place a precise squiggle.

## Formatter

```csharp
public static class MessageFormatter
{
    public static string Format(Message message, CultureInfo culture,
        IReadOnlyDictionary<string, object?> arguments);
}
```

Rules:

- **Plural/selectordinal resolution:** compute the operand set from the numeric argument, then resolve the CLDR category via the embedded rules (cardinal for `plural`, ordinal for `selectordinal`). An explicit `=N` selector wins over a category when the value matches exactly. `#` renders the number (minus `offset`) using `culture`'s number format.
- **Select resolution:** exact string match on the argument's value, else `other`.
- **Simple/typed arguments:** format with `culture` via `System.Globalization` (numbers, dates) or `ToString` honoring `IFormattable` with the requested style.
- **Missing argument:** define one policy and keep it — recommended: render the placeholder name in braces unchanged and (in debug builds) surface a diagnostic, rather than throwing, so a missing runtime argument never crashes a user interface. Make the throw-vs-passthrough behavior a formatter option defaulting to passthrough.
- **Performance:** parsing is the cost; rendering should not re-parse. The runtime (spec 05) caches parsed `Message` instances per (key, culture). The formatter itself allocates a single `StringBuilder` and is allocation-conscious on the common (literal + a few args) path.

## CLDR plural rules (the embedded data)

This is the one input that cannot be hand-rolled away and must not be hand-maintained per language.

- **Source:** the Unicode CLDR plural rules — cardinal (`plurals.xml`) and ordinal (`ordinals.xml`). Check the relevant data into the repository at a pinned CLDR version, with the version recorded.
- **Operands:** implement the CLDR operand model exactly — `n` (absolute value), `i` (integer digits), `v` (visible fraction digit count, with trailing zeros), `w` (visible fraction digits without trailing zeros), `f` (visible fraction digits as integer, with trailing zeros), `t` (without trailing zeros), `e`/`c` (compact/exponent). Cardinal rules for many languages depend on `v` and `f`, not just `n`; getting only `n` right silently breaks languages like Polish and Czech for fractional values.
- **Generation:** at build time, transform the CLDR rule expressions into C# — either generated `switch`/predicate methods per language, or a compact compiled rule table evaluated by a small interpreter. Generated output is checked in or produced by a generator project; the runtime depends only on the generated code, not on any Extensible Markup Language parsing at runtime.
- **Application Programming Interface:**

```csharp
public static class PluralRules
{
    public static PluralCategory Cardinal(string culture, PluralOperands operands);
    public static PluralCategory Ordinal(string culture, PluralOperands operands);
    public static PluralOperands Operands(decimal value, int? minFractionDigits = null);
}
```

- **Fallback:** resolve `culture` by walking to its base language (`de-AT` → `de`), and ultimately to the CLDR `root` rule set (`other` only) for an unknown language, so an unrecognized culture never throws.
- **Gettext bridge:** expose enough to let the Portable Object provider (spec 03) compute the gettext `Plural-Forms` `nplurals`/`plural=` expression-equivalent ordering for a locale, so ICU categories map deterministically to gettext indices. Provide a helper `IReadOnlyList<PluralCategory> GettextOrder(string culture)` giving the category order gettext expects for that language.

## Acceptance criteria

- [ ] Round-trip `Parse`→serialize (a `ToString` on `Message`) reproduces a semantically equal message for all supported constructs, including correct ICU apostrophe quoting.
- [ ] `Validate` flags a missing `other`, a stray `#`, duplicate selectors, and invalid category keywords, each with a character offset.
- [ ] `ExtractPlaceholders` returns exactly the argument names used, including those used only inside nested branches and the `select`/`plural` argument itself.
- [ ] Plural resolution matches the CLDR test data for a representative spread of languages (at minimum English, Polish, Czech, Russian, Arabic, Welsh, Japanese) across integers and fractional values, for both cardinal and ordinal.
- [ ] Operand computation is correct for fractional values (e.g., distinguishes `1.0` from `1` where `v` matters).
- [ ] The formatter renders `#` with the target culture's number formatting and applies `offset`.
- [ ] A missing argument does not throw under the default policy.
- [ ] No runtime dependency reads the CLDR Extensible Markup Language; all plural logic is in generated code, and the embedded CLDR version is recorded in the assembly.

## Regenerating the CLDR plural data

The plural rules are generated from pinned CLDR data checked into `eng/cldr/`. No CLDR data is parsed
at runtime — all plural logic is generated code. To bump the version, replace the JSON files and run:

```bash
python3 eng/cldr/generate_plural_data.py
```

which regenerates `src/Localization.MessageFormat/Internal/CldrPluralData.g.cs`. The embedded CLDR
release is recorded in the assembly and exposed as `PluralRules.CldrVersion`.
