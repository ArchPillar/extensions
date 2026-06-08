# Localization.BlazorSample

Demonstrates ArchPillar.Extensions.Localization in a server-rendered Blazor app, with components injecting both the Localizer and the IStringLocalizer adapter and switching culture per navigation.

## What it shows
- Registering both the Localizer and the `IStringLocalizer` adapter with `AddArchPillarLocalization`
- Components injecting the Localizer for ICU plurals and the `IStringLocalizer` adapter for keyed lookups
- Request-culture middleware reading `?culture=` so the per-page culture-switch links work on each navigation
- The `IStringLocalizer` path returning the key with `ResourceNotFound` when an entry is missing (the failure path)

## Running
```bash
dotnet run --project samples/Localization/Localization.BlazorSample
```
Starts a web server (the console prints the URL, e.g. `http://localhost:5xxx`). Open `/` for the
inbox page, then use the English / Deutsch links — or hit `/?culture=de` directly — to see the
title, pluralized counts, and summary switch culture.

## Notes
Each server-rendered navigation is a fresh request, so the `?culture=` links re-run the
request-culture middleware and re-render in the chosen culture. The German catalog is
`Translations/de.arb`.
