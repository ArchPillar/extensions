# ArchPillar.Extensions.Localization

A user-interface translation library for .NET. Write each translatable string once, at the call site,
as an in-code default in ICU MessageFormat; a Roslyn generator extracts it at compile time for
translators; at runtime, translated catalogs (ARB, XLIFF 2.1, or Portable Object) load as pluggable
overrides. The in-code default is always the source of truth and the terminal fallback, so an app
with **zero** translation files runs correctly and partial translations degrade gracefully key by key.

## Why?

The usual `.resx` + `IStringLocalizer` flow inverts the source of truth: the key is a lookup into a
resource file, and the call site says nothing about what the string actually is. Strings drift from
their translations, a missing resource yields the bare key, and nothing checks at build time that a
string was extracted or that its placeholders line up.

This library puts the **source string at the call site** and makes the build do the bookkeeping. The
default renders even with no catalogs loaded — there is no "missing resource" state, only "no override
yet" — while a source generator extracts every call site and an analyzer flags a non-constant key,
invalid ICU, or a placeholder with no argument before you ship. Translations layer over the defaults
in one ambient store that needs no DI, so even an exception thrown before any container exists localizes.

## Quick Start

```csharp
using static ArchPillar.Extensions.Localization.Localizer;
using System.Globalization;

// Translate anywhere with a using static, the way `using static System.Console;` gives you WriteLine.
// "greeting" is the key; "Hello {name}!" is the in-code default (the source text and the fallback).
string Greet(string name) => Translate("greeting", "Hello {name}!", ("name", name));

Console.WriteLine(Greet("Ada"));                  // "Hello Ada!" (the in-code default)

CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
Console.WriteLine(Greet("Ada"));                  // "Hallo Ada!" once a de catalog is loaded
```

`Translate` goes through the process-wide ambient store, reachable with no services (or call
`Localizer.Default.Translate(...)` without the `using static`). Translations come from a
`Translations/de.arb` file beside the binary (loaded automatically), an embedded catalog, or
`Localizer.AddCatalog(...)`. Scope keys by category with `ILocalizer<T>` as an app grows.

## Dependency injection and IStringLocalizer

In a host, add the `ArchPillar.Extensions.Localization.DependencyInjection` package and register the
library; it feeds the same ambient store and registers `ILocalizer`, `ILocalizer<T>`, and a composing
`IStringLocalizer` adapter (which falls through to an existing `.resx` factory, easing migration):

```csharp
services.AddArchPillarLocalization(new LocalizerOptions { SourceCulture = "en" });
```

`UseRequestLocalization` drives the culture in ASP.NET Core — no extra wiring.

## What's in the box

- **Categories** — `ILocalizer<T>` scopes keys by `typeof(T)`, the `ILogger<T>` model; no namespaces to manage.
- **The ambient store** — one process-wide, layered, DI-free store reachable from anywhere, or an
  instantiable `LocalizationContext` (with `UseAmbient = false`) for a fully static-free setup.
- **ICU MessageFormat** — arguments, `plural`/`selectordinal`/`select`, embedded CLDR plural data.
- **Standard formats** — ARB (default), XLIFF 2.1, Portable Object, bundled (no extra packages).
- **Files / embedded / satellites** — loose files by default (trim/AOT-safe); opt-in satellite embedding.
- **Tooling** — a source generator, the `APL` analyzer diagnostics, and the `dotnet apl` CLI, all bundled.

No dependencies beyond the Base Class Library.

## Documentation

See the [localization documentation](https://github.com/ArchPillar/extensions/tree/main/docs/localization)
for getting started, every feature, production recommendations, and the design spec.

## License

MIT. See [LICENSE](https://github.com/ArchPillar/extensions/blob/main/LICENSE).
