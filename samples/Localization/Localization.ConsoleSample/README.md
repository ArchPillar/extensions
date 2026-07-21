# Localization.ConsoleSample

Demonstrates ArchPillar.Extensions.Localization in a generic-host console app, with the Localizer resolved from DI and an English in-code default overridden by a German catalog.

## What it shows
- Registering the Localizer in DI with `AddArchPillarLocalization` and resolving `ILocalizer` as a service
- In-code English default overridden at runtime by a German `.xliff` catalog beside the binary
- Named arguments (`{name}`) and ICU plurals (`{count, plural, ...}`) across both cultures
- English needs no file: the in-code default is the source of truth and the terminal fallback
- A `{amount, number, ...}` value in a message formats in the culture the string renders in — the
  source culture for an untranslated default, the target culture once a translation exists
  (`cart.total`, translated to German); `ToLocalizedString` takes the culture explicitly, so a
  standalone value (a price, its full currency name, a compact form) always formats in the given
  culture regardless of translation state

## Running
```bash
dotnet run --project samples/Localization/Localization.ConsoleSample
```
Prints an `--- en ---` and an `--- de ---` block, each showing the greeting, the pluralized inbox
count for 0, 1, and 2 messages, three standalone `ToLocalizedString` values (a price, its full
currency name, a compact form), and one translated in-message total (`cart.total`). Under
`--- de ---` the standalone values and the translated total render with German digit
grouping/decimal marks and the symbol joined by a space (shown below as a normal space):

> The space between the amount and `$` in the German lines is a non-breaking space (U+00A0).

```text
--- en ---
Hello Ada
No messages
1 message
2 messages
Price:      $1,234.56
Full name:  1,234.56 US dollars
Compact:    $1.2K
Total: $1,234.56
--- de ---
Hallo Ada
Keine Nachrichten
1 Nachricht
2 Nachrichten
Price:      1.234,56 $
Full name:  1.234,56 US-Dollar
Compact:    1234 $
Gesamt: 1.234,56 $
```

## Notes
The German catalog is `Translations/de.xliff`, copied beside the binary and loaded as an override
at runtime; English lives in code, so it has no file. `cart.total` is the one message key with a
German translation, which is what lets its number render in German — an untranslated message
always renders its `{..., number, ...}` values in the source culture, no matter which culture is
requested; only a translated string (or `ToLocalizedString`'s explicit culture) reaches the target
culture's number formatting.
