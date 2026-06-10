# ArchPillar.Extensions.Localization.DependencyInjection

Optional dependency-injection registration for `ArchPillar.Extensions.Localization`. The core runtime stays
dependency-free; reference this package when you want DI registration of the native localization views.

```csharp
services.AddArchPillarLocalization(new LocalizerOptions { SourceCulture = "en" });
// ...
public sealed class Home(ILocalizer<Home> localizer)
{
    public string Title => localizer.Translate("home.title", "Home");
}
```

`AddArchPillarLocalization` configures the ambient store from your `LocalizerOptions` and registers
`ILocalizer`, `ILocalizer<T>`, and a concrete `Localizer` over it, so an injected localizer, a non-DI caller,
and an exception text all read the same store.

Migrating from `IStringLocalizer`/`.resx`? Add the
[`ArchPillar.Extensions.Localization.StringLocalizer`](https://www.nuget.org/packages/ArchPillar.Extensions.Localization.StringLocalizer)
package and call `AddArchPillarStringLocalizer` instead — it performs this registration and additionally adapts
the localizer to `IStringLocalizer`/`IStringLocalizer<T>`, composing over your existing `.resx`. Drop it once
your code no longer depends on `IStringLocalizer`.
