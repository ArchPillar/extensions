# Getting started with ArchPillar.Extensions.Localization

A walkthrough from installing the package to rendering your first translated string. By the end you
will have an English default that ships in code and a German override that loads from a file.

## 1. Install

```bash
dotnet add package ArchPillar.Extensions.Localization
```

Referencing the package also activates the analyzer and the source generator — no extra setup.

### SDK requirement

The analyzer and generator are built against a modern Roslyn, so the **build** needs a recent .NET SDK —
roughly **.NET SDK 9.0.3xx or newer** (Visual Studio 17.14+, or any .NET 10 SDK). This is independent of
your *target framework*: a project targeting `net8.0` builds fine, it just has to be built with a new
enough SDK. On an older SDK the package still restores and the runtime still works, but **extraction and
the analyzer silently do nothing** — no template is generated and no `APL` diagnostics appear. If your
keys are not being extracted, check `dotnet --version` first.

## 2. Write a translatable call site

Inject `ILocalizer<T>`; the type argument is the *category* that scopes your keys (the `ILogger<T>`
model). Default it to the ambient store so the type works with or without DI:

```csharp
using ArchPillar.Extensions.Localization;

public sealed class Greeter(ILocalizer<Greeter>? localizer = null)
{
    private readonly ILocalizer<Greeter> _localizer = localizer ?? Localization.For<Greeter>();

    // key, in-code ICU default, then (name, value) arguments.
    public string Greet(string name) => _localizer.Translate("greeting", "Hello {name}!", ("name", name));

    public string Inbox(int count) =>
        _localizer.Translate("inbox", "You have {count, plural, one {# message} other {# messages}}", ("count", count));
}
```

## 3. Run with the in-code defaults

With no catalogs, every string renders its in-code default — the app is fully functional in the
source language:

```csharp
var greeter = new Greeter();
Console.WriteLine(greeter.Greet("Ada"));   // "Hello Ada!"
Console.WriteLine(greeter.Inbox(1));       // "You have 1 message"
Console.WriteLine(greeter.Inbox(5));       // "You have 5 messages"
```

## 4. Add a translation

Create a `Translations/` folder beside your project and add `de.arb` (ARB is the default format). The
`x-category` matches the type's full name, and the keys match the call sites:

```json
{
  "@@locale": "de",
  "greeting": "Hallo {name}!",
  "@greeting": { "x-category": "YourNamespace.Greeter", "x-state": "Translated" },
  "inbox": "Sie haben {count, plural, one {# Nachricht} other {# Nachrichten}}",
  "@inbox": { "x-category": "YourNamespace.Greeter", "x-state": "Translated" }
}
```

Copy it to the output so it sits beside the binary:

```xml
<ItemGroup>
  <Content Include="Translations\**\*.arb" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

> `x-category` must equal the type's full name **exactly** (namespace included). If it does not match —
> or the key does not — the lookup misses and the string silently renders its in-code default rather than
> erroring. This is the main reason a hand-authored override "does nothing"; the generated files below get
> it right for you.

> In a normal build the generator also emits a source-language template from your call sites, and the
> `dotnet apl` tool turns that into per-language files for translators — so you usually do not
> hand-author the catalog. This step shows the shape of what lands beside the binary.

## 5. Switch culture and see the override

The ambient store loads `Translations/` automatically and resolves against `CurrentUICulture`:

```csharp
using System.Globalization;

CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
Console.WriteLine(greeter.Greet("Ada"));   // "Hallo Ada!"
Console.WriteLine(greeter.Inbox(5));        // "Sie haben 5 Nachrichten"
```

## 6. (Optional) Wire up dependency injection

In a host, register the library and inject `ILocalizer<T>`. `UseRequestLocalization` drives the culture in
ASP.NET Core:

```csharp
builder.Services.AddArchPillarLocalization(new LocalizerOptions { SourceCulture = "en" });
```

DI feeds the same ambient store, so injected and ambient lookups share one source. Migrating existing
`IStringLocalizer` code? Add the `…Localization.StringLocalizer` package and call
`AddArchPillarStringLocalizer` instead — see the migration on-ramp in [features.md](features.md).

## Next

- [features.md](features.md) — categories, the ambient store, embedding and satellites, formats, ICU
  plurals, DI, and the `IStringLocalizer` migration on-ramp.
- [recommendations.md](recommendations.md) — production patterns and the trimming / AOT guidance.
