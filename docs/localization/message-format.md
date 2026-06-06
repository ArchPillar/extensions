# MessageFormat — usage

`ArchPillar.Extensions.Localization.MessageFormat` parses, validates, and renders
[ICU MessageFormat](https://unicode-org.github.io/icu/userguide/format_parse/messages/), with
plural categories resolved from embedded Unicode CLDR data. It has no dependencies beyond the BCL
and is usable on its own. The parsed syntax tree is an internal detail; the public surface is the
three operations below.

> Design reference: [`04-message-format-and-plurals.md`](04-message-format-and-plurals.md).

## Formatting

```csharp
using System.Globalization;
using ArchPillar.Extensions.Localization.MessageFormat;

var formatter = new MessageFormatter(); // reusable, thread-safe, caches parsed templates

string text = formatter.Format(
    "You have {count, plural, =0 {no messages} one {# message} other {# messages}}",
    CultureInfo.GetCultureInfo("en"),
    ("count", 5));
// "You have 5 messages"
```

Plural categories use the **target** culture, so the same template pluralizes correctly per
language:

```csharp
var template = "{count, plural, one {# plik} few {# pliki} many {# plików} other {# pliku}}";
formatter.Format(template, CultureInfo.GetCultureInfo("pl"), ("count", 5)); // "5 plików" (many)
```

Supported constructs: plain text with ICU apostrophe quoting, `{name}`, `{name, number|date|time, style}`,
`plural`/`selectordinal` (with `offset`, `=N` selectors, and `#`), `select`, and arbitrary nesting.

### Missing arguments

By default a missing argument renders its placeholder unchanged (`{name}`) and never throws. Pass
`MissingArgumentPolicy.Throw` to opt into a `MissingArgumentException` instead.

## Validation and placeholders

```csharp
MessageSyntax.TryValidate("{count, plural, one {x}}", out MessageFormatError? error);
// false; error.Position points at the offending offset (no 'other' branch is a validation concern)

MessageSyntax.ExtractPlaceholders("{greeting}, {name}! {count, plural, other {#}}");
// ["greeting", "name", "count"]
```

## Plural rules

```csharp
PluralRules.Cardinal("ru", PluralRules.Operands(5)); // PluralCategory.Many
PluralRules.Ordinal("en", PluralRules.Operands(21)); // PluralCategory.One  (21st)
PluralRules.CldrVersion;                              // the pinned CLDR release
```

`Operands` implements the full UTS&nbsp;#35 operand model (`n i v w f t e c`), so fractional values
resolve correctly (for example English `1.0` is `other`, not `one`).

## CLDR data

The plural rules are generated from pinned CLDR data checked into `eng/cldr/`. To bump the version,
replace the JSON files and run `python3 eng/cldr/generate_plural_data.py`, which regenerates
`src/Localization.MessageFormat/Internal/CldrPluralData.g.cs`. No CLDR data is parsed at runtime.
