# ArchPillar.Extensions.Localization.MessageFormat

An [ICU MessageFormat](https://unicode-org.github.io/icu/userguide/format_parse/messages/)
parser, validator, and formatter for .NET, with embedded Unicode CLDR plural rules. This is
the shared message grammar used across the `ArchPillar.Extensions.Localization` family — and
is usable on its own wherever ICU MessageFormat rendering or CLDR plural categories are needed
(.NET's `System.Globalization` does not expose CLDR plural categories).

Zero dependencies beyond the Base Class Library.

## Supported grammar

- Plain text with ICU apostrophe quoting (`'{'`, `''`)
- Simple and typed arguments (`{name}`, `{name, number}`, `{name, date, long}`)
- `plural` and `selectordinal` with `offset`, explicit `=N` selectors, the `#` token, and
  CLDR keyword categories (`zero one two few many other`)
- `select` with arbitrary string keys
- Arbitrary nesting of constructs inside branches

See the [localization documentation](https://github.com/ArchPillar/extensions/tree/main/docs/localization)
for the full specification.
