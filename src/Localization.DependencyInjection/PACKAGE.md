# ArchPillar.Extensions.Localization.DependencyInjection

Optional dependency-injection and `IStringLocalizer` integration for
`ArchPillar.Extensions.Localization`. The core runtime stays dependency-free; reference this package
when you want DI registration or `IStringLocalizer` interop.

```csharp
services.AddArchPillarLocalization(new LocalizerOptions { SourceCulture = "en" });
// ...
public sealed class Home(IStringLocalizer<Home> localizer)
{
    public string Title => localizer["home.title"];      // override for the request culture, or "home.title"
}
```

The adapter follows the standard `IStringLocalizer` model: the name is the key, and a missing entry
returns the name with `ResourceNotFound = true`. Positional arguments map to `{0}`, `{1}`, … ICU
placeholders. The request culture is `CultureInfo.CurrentUICulture`, which `UseRequestLocalization()`
sets per request.
