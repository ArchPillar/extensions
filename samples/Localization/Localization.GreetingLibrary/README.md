# Localization.GreetingLibrary

A batteries-included class library that ships its German translations embedded as a culture satellite, so a consuming app needs no configuration, no DI, and no files on disk.

## What it shows

- Defaulting to the ambient store so a no-DI caller can just `new Greeter()` and have translations work, while still accepting an injected `ILocalizer<T>` for DI hosts
- Shipping the German catalog inside the library's own DLL as a culture satellite (`Culture="de"`), discovered lazily via `LocalizationSatelliteCatalogsAttribute`
- A localized exception message resolved from the ambient store with no services available — the case that rules out DI (works in a static constructor or before any container is built)

## Consumed by

This is a class library, not an executable, so there is no `Program.cs` and nothing to run directly. It is consumed by [Localization.LibraryConsumer](../Localization.LibraryConsumer/README.md), which calls it with zero configuration; run that sample to see the library in action.

## Notes

The English defaults ship in code (`Greeter.cs`, `Validator.cs`); the German override is `Translations/de.arb`, embedded with `Culture="de"`.
