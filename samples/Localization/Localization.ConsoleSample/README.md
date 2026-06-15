# Localization.ConsoleSample

Demonstrates ArchPillar.Extensions.Localization in a generic-host console app, with the Localizer resolved from DI and an English in-code default overridden by a German catalog.

## What it shows
- Registering the Localizer in DI with `AddArchPillarLocalization` and resolving it as a service
- In-code English default overridden at runtime by a German `.xliff` catalog beside the binary
- Named arguments (`{name}`) and ICU plurals (`{count, plural, ...}`) across both cultures
- English needs no file: the in-code default is the source of truth and the terminal fallback

## Running
```bash
dotnet run --project samples/Localization/Localization.ConsoleSample
```
Prints an `--- en ---` and an `--- de ---` block, each showing the greeting and the pluralized
inbox count for 0, 1, and 2 messages.

## Notes
The German catalog is `Translations/de.xliff`, copied beside the binary and loaded as an override
at runtime; English lives in code, so it has no file.
