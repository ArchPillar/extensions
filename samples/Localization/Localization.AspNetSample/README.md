# Localization.AspNetSample

Demonstrates ArchPillar.Extensions.Localization in an ASP.NET Core minimal API, exposing both the Localizer and the IStringLocalizer adapter over HTTP with per-request culture.

## What it shows
- Registering both the Localizer and the `IStringLocalizer` adapter with `AddArchPillarLocalization`
- ASP.NET request-culture middleware driving the active culture from the `?culture=` query string
- The Localizer at `/`: named arguments and ICU plurals, in-code English overridden by `de.xliff`
- The `IStringLocalizer` adapter at `/strings`, where a missing entry returns the key with `ResourceNotFound` set (the failure path)
- `[Localized]` annotations at `/form`: field labels and hints declared on the model itself and read back with the `MemberInfo` helpers — the path that needs neither MVC model metadata nor `IStringLocalizer`. A field with no annotation falls back to its own member name (the failure path)

## Running
```bash
dotnet run --project samples/Localization/Localization.AspNetSample
```
Starts a web server (the console prints the URL, e.g. `http://localhost:5xxx`). Hit `/?culture=de`
for the localized greeting and inbox count, `/strings?culture=de` for the `IStringLocalizer`
result, and `/form?culture=de` for the annotated field labels and hints:

```json
{"email":{"label":"E-Mail-Adresse","hint":"Wir geben sie nicht weiter."},
 "password":"Passwort","nickname":"Nickname"}
```

## Notes
Culture is selected per request via the `?culture=` query parameter (`en` or `de`); without it the
default culture is English. The German catalog is `Translations/de.xliff`. At `/strings?culture=en` the
entry has no override, so the response carries `resourceNotFound: true` and echoes the key.

`/form` shows that an annotation *is* the translation site — nothing calls the localizer with a literal
key, yet the strings still reach the catalog, because extraction reads the attributes out of the built
assembly. A description's key is derived from its display key (`user.email` → `user.email.description`),
so a field never repeats its own id, and `nickname` carries no annotation at all, so it renders its own
member name rather than a null.
