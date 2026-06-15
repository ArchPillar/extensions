# Localization.MigrationSample

Migrates an app that already localizes with `AddLocalization()` + real `.resx` onto ArchPillar without rewriting call sites.

## What it shows
- An `IStringLocalizer` adapter that composes over the legacy `ResourceManager`, so existing `.resx` translations keep resolving.
- ArchPillar's `Translations/de.xliff` entry winning where it has one (`Welcome`), falling through to the `.resx` where it doesn't (`Goodbye`), then to the name (`Help`).
- The `L("...")` marker, a no-op at runtime that flags strings for extraction.

## Running
```bash
dotnet run --project samples/Localization/Localization.MigrationSample
```
Prints `en` and `de` blocks: `Welcome` comes from the new `de.xliff`, `Goodbye` from the legacy `.resx`, and `Help` falls back to the key; ends with the `L(...)` marker line.

## Notes
`AssemblyName` is pinned to `RootNamespace` (`MigrationSample`) so `ResourceManager` finds the `.resx` — the documented gotcha when the two diverge.
