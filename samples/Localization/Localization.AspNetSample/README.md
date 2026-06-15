# Localization.AspNetSample

Demonstrates ArchPillar.Extensions.Localization in an ASP.NET Core minimal API, exposing both the Localizer and the IStringLocalizer adapter over HTTP with per-request culture.

## What it shows
- Registering both the Localizer and the `IStringLocalizer` adapter with `AddArchPillarLocalization`
- ASP.NET request-culture middleware driving the active culture from the `?culture=` query string
- The Localizer at `/`: named arguments and ICU plurals, in-code English overridden by `de.xliff`
- The `IStringLocalizer` adapter at `/strings`, where a missing entry returns the key with `ResourceNotFound` set (the failure path)

## Running
```bash
dotnet run --project samples/Localization/Localization.AspNetSample
```
Starts a web server (the console prints the URL, e.g. `http://localhost:5xxx`). Hit `/?culture=de`
for the localized greeting and inbox count, and `/strings?culture=de` for the `IStringLocalizer`
result.

## Notes
Culture is selected per request via the `?culture=` query parameter (`en` or `de`); without it the
default culture is English. The German catalog is `Translations/de.xliff`. At `/strings?culture=en` the
entry has no override, so the response carries `resourceNotFound: true` and echoes the key.
