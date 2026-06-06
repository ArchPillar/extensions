# Integration — dependency injection & IStringLocalizer

`ArchPillar.Extensions.Localization.DependencyInjection` is the **optional** adapter package (Decision
D-5). The core runtime stays dependency-free; reference this package only when you want DI registration
or `IStringLocalizer` interop.

## Registration

```csharp
services.AddArchPillarLocalization(new LocalizerOptions
{
    TranslationsDirectory = "Translations",
    SourceCulture = "en"
});
```

This registers a singleton `Localizer` and adapts it to `IStringLocalizer`, `IStringLocalizer<T>`, and
`IStringLocalizerFactory`. Inject whichever you prefer:

```csharp
public sealed class HomeModel(IStringLocalizer<HomeModel> localizer)
{
    public string Title => localizer["home.title"];          // override for the request culture, else "home.title"
    public string Inbox(int n) => localizer["inbox.count", n]; // positional arg maps to {0}
}

// or use the Localizer directly for the full key + in-code default + named arguments:
public sealed class Greeter(Localizer localizer)
{
    public string Greet(string name) =>
        localizer.Translate("home.greeting", "Hello {name}", ("name", name));
}
```

## The IStringLocalizer model

`IStringLocalizer` has no separate key vs. in-code default, so the adapter follows its standard model:

- **The name is the key.** `localizer["home.title"]` looks up the override for the current culture.
- **Missing → name.** With no override, it returns the name with `ResourceNotFound = true`.
- **Positional arguments map to ICU `{0}`, `{1}`, …** — so `IStringLocalizer`-authored messages use
  positional placeholders. (Use the `Localizer` directly for named placeholders and an in-code default.)
- The resource type of `IStringLocalizer<T>` is ignored: keys are one global symbolic namespace.

## ASP.NET Core

No extra wiring is needed for request culture. `Localizer` reads `CultureInfo.CurrentUICulture`, which
`app.UseRequestLocalization(...)` sets per request — so the standard ASP.NET culture middleware drives
which override is used.

## Samples

Three runnable samples under `samples/Localization` show the same registration across hosting models:

- **`Localization.ConsoleSample`** — a generic `Host` resolving the `Localizer` from DI (named arguments,
  ICU plurals, in-code English default, German override).
- **`Localization.AspNetSample`** — a minimal API with `UseRequestLocalization`; one endpoint injects the
  `Localizer`, another the `IStringLocalizer<T>` adapter. Switch culture with `?culture=de`.
- **`Localization.BlazorSample`** — a server-rendered Razor component injecting both `Localizer` and
  `IStringLocalizer<Home>`, with culture-switch links. It also shows the honest "missing → name" behaviour
  for the source language under the `IStringLocalizer` model.
