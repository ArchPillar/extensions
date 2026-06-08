# Localization.TodoSample

Demonstrates ArchPillar.Extensions.Localization in a no-DI console app using a self-scoped string bundle, with a pseudo-localization QA pass to catch hardcoded text.

## What it shows

- A self-scoped `Localized<T>` string bundle where the member name is the key and the type is the category
- In-code English overridden by German and French `.arb` catalogs beside the binary
- ICU plurals (`{count, plural, ...}`) resolved per culture
- A pseudo-localization QA culture (`qps-ploc`) that X's translatable strings, so anything still readable is not going through the localizer

## Running

```bash
dotnet run --project samples/Localization/Localization.TodoSample
```

Prints the to-do list four times — once each for `en`, `de`, `fr`, and `qps-ploc`. Under the pseudo culture the localized strings are X'd out while the hardcoded task titles and checkbox glyphs stay readable, which is the point of the smoke test.

## Notes

The string bundle lives in `TodoStrings.cs`; the catalogs are `Translations/de.arb` and `Translations/fr.arb`.
