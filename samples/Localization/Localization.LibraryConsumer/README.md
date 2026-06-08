# Localization.LibraryConsumer

Demonstrates ArchPillar.Extensions.Localization consuming a batteries-included library with zero configuration — no DI, no setup, no files on disk.

## What it shows

- Calling a localized library with zero configuration — no DI, no `AddArchPillarLocalization`, no files placed on disk
- German resolved from the library's embedded culture satellite via the ambient store
- A localized exception message thrown with no services available (the case that rules out DI)

## Running

```bash
dotnet run --project samples/Localization/Localization.LibraryConsumer
```

Prints the greeting and a validation message twice — once for `en` and once for `de`. English comes from code; German comes from the library's embedded catalog, including the localized exception text.

## Notes

This is the runnable half of a deliberate pair: the library it consumes is [Localization.GreetingLibrary](../Localization.GreetingLibrary/README.md), which ships German embedded as a satellite. There is no localization setup in this project at all.
