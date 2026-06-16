# Localization — ICU MessageFormat and diagnostics

## ICU MessageFormat

Defaults and translations are written in **ICU MessageFormat** — the grammar `.po`/`.arb`
translators already use — so a string carries its own grammar instead of relying on concatenation
that breaks in other languages. The full surface is supported:

- **Simple arguments:** `"Hello {name}"`.
- **Typed formatting:** `"{amount, number, currency}"`, `"{when, date, short}"`, `"{t, time}"`.
- **`plural` / `selectordinal`:** with `offset`, `=N` exact-match selectors, and `#` for the
  formatted count.
- **`select`:** arbitrary categories (e.g. gender).
- **Arbitrary nesting** of all of the above.

```csharp
localizer.Translate("inbox",
    "You have {count, plural, =0 {no messages} one {# message} other {# messages}}", ("count", 5));
// → "You have 5 messages"
```

**Plural categories resolve against the target culture** from embedded Unicode CLDR data: the one
template above pluralises by English rules under `en`, and by Polish rules (`one`/`few`/`many`/
`other`) under `pl`, with no per-language code. Never branch in C# (`if (n == 1) …`) for plurals —
that hardcodes one language's rules.

**Missing arguments:** by default a referenced placeholder with no supplied value renders unchanged
and never throws, so a partial call still produces readable output. Switch
`MissingArgumentPolicy.Throw` in the options to fail fast instead.

> The grammar is implemented by the supporting `ArchPillar.Extensions.Localization.MessageFormat`
> library (pulled in automatically). It is dependency-free and *technically* usable on its own —
> `MessageFormatter.Format` to render, `MessageSyntax.TryValidate` / `ExtractPlaceholders` to lint a
> template, `PluralRules` for raw CLDR categories — but standalone use is a niche case, not part of
> normal localization work.

## Compile-time diagnostics

A translatable call site is recognised by the `[Translatable]` / `[TranslationDefault]` parameter
attributes (not by name), so `Translate(...)`, the indexer, `L(...)`, and your own wrapper methods
are all checked the same way. The analyzer surfaces these in the editor as you type:

| Diagnostic | Meaning |
|------------|---------|
| `APL0001` | A translatable key/default is **not a compile-time constant** (error). |
| `APL0002` | The default is **not valid ICU MessageFormat**. |
| `APL0003` / `APL0004` | A placeholder has **no argument** / an argument is **unused**. |
| `APL0005` | A `plural`/`select` is **missing its `other` branch**. |
| `APL0006` / `APL0007` | A **duplicate key with conflicting text** / **identical text under different keys**. |
| `APL0008` | A key does **not match the configured pattern**. |
| `APL0010` | A DI consumer's `Localized<>` bundle is **not `partial`**, so its constructor and registration cannot be generated (one-click fix marks it `partial`). |

These are call-site diagnostics, not runtime surprises — the design fails fast at build time and
**never** at runtime (a miss always renders the in-code default).
