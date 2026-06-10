# ArchPillar.Extensions.Localization.StringLocalizer

`IStringLocalizer` interop for `ArchPillar.Extensions.Localization` — a **migration on-ramp**. Reference
this package while adopting the library from an existing `IStringLocalizer`/`.resx` codebase; it adapts the
localizer to `IStringLocalizer`/`IStringLocalizer<T>` and **composes over** your existing
`ResourceManager`/`.resx` factory, so strings the ambient store does not yet have keep resolving from
`.resx`. Once your code no longer depends on `IStringLocalizer`, drop this package and keep the native
`AddArchPillarLocalization` registration.

```csharp
services.AddArchPillarStringLocalizer(new LocalizerOptions { SourceCulture = "en" });
// ...
public sealed class Home(IStringLocalizer<Home> localizer)
{
    public string Title => localizer["home.title"];      // override for the request culture, else .resx, else "home.title"
}
```

`AddArchPillarStringLocalizer` performs the native `AddArchPillarLocalization` registration for you, so a
single call wires up both the native views and the interop adapters. It also registers the
`ResourceManager`/`.resx` factory itself, so `.resx` keeps resolving regardless of whether you call
`AddLocalization()` before or after it.

The adapter follows the standard `IStringLocalizer` model: the name is the key, and a missing entry returns
the name with `ResourceNotFound = true`. Positional arguments map to `{0}`, `{1}`, … ICU placeholders. The
request culture is `CultureInfo.CurrentUICulture`, which `UseRequestLocalization()` sets per request.
