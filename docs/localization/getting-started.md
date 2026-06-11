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

## 4. Generate the translator files

You do **not** hand-author catalogs — the `dotnet apl` tool produces them from your code, filling in the
category, placeholders, and source fingerprints for you. Install it once:

```bash
dotnet tool install --global ArchPillar.Extensions.Localization.Tooling   # command: dotnet apl
```

Build, then create a language file for the translator:

```bash
dotnet build
# Add German across everything in the solution that has strings:
dotnet apl add de --solution YourApp.sln --output Translations
#   -> Translations/YourApp.de.arb  (every entry x-state: NeedsTranslation)
```

`YourApp.de.arb` is what you hand off. The translator fills in the German and marks each entry
`Translated`; you commit the file. The catalogs are named `{AssemblyName}.{culture}.arb` so libraries
never collide, and the build copies them beside the binary automatically.

> **Smaller scopes:** `--project YourApp.csproj` (add `--recurse` to include its project dependencies) or
> `--input bin/Debug/net10.0` instead of `--solution`. Run `dotnet apl status --solution YourApp.sln` to
> see which assemblies have strings and how many.

> When you reference the package, the build also runs `extract` for you after each real build, so the
> source-language template (`{AssemblyName}.en.arb`) appears in `Translations/` without you asking. As code
> changes, `dotnet apl sync --solution YourApp.sln --output Translations` reconciles every language file
> (and `--check` is your CI gate). The full lifecycle — including handing files to translators as a zip and
> shipping for production — is in [translation-workflow.md](translation-workflow.md).

## 5. Switch culture and see the override

The ambient store loads `Translations/` automatically and resolves against `CurrentUICulture`:

```csharp
using System.Globalization;

CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
Console.WriteLine(greeter.Greet("Ada"));   // "Hallo Ada!"
Console.WriteLine(greeter.Inbox(5));        // "Sie haben 5 Nachrichten"
```

## 6. (Optional) Wire up dependency injection

DI registration lives in a separate package:

```bash
dotnet add package ArchPillar.Extensions.Localization.DependencyInjection
```

In a host, register the library and inject `ILocalizer<T>`. `UseRequestLocalization` drives the culture in
ASP.NET Core:

```csharp
builder.Services.AddArchPillarLocalization(new LocalizerOptions { SourceCulture = "en" });
```

DI feeds the same ambient store, so injected and ambient lookups share one source. Migrating existing
`IStringLocalizer` code? Add the `…Localization.StringLocalizer` package and call
`AddArchPillarStringLocalizer` instead — see the migration on-ramp in [features.md](features.md).

## 7. Ship it

In development each library keeps its own `{AssemblyName}.{culture}.arb` files, which the build copies
beside the binary. On **publish**, the build flattens them into one bundle per culture (`de.arb`, `fr.arb`,
…) so production ships a handful of files instead of one per library — automatically, no configuration. For
single-file or NativeAOT publish, opt into embedding instead (`ArchPillarLocalizationEmbedTargets=true`).
See [translation-workflow.md](translation-workflow.md#deployment) for the details and the trim/AOT matrix.

## Next

- [translation-workflow.md](translation-workflow.md) — the full lifecycle: scopes, `status`/`extract`/`add`/
  `sync`, the translator handoff (export/import), and deployment.
- [features.md](features.md) — categories, the ambient store, embedding and satellites, formats, ICU
  plurals, DI, and the `IStringLocalizer` migration on-ramp.
- [recommendations.md](recommendations.md) — production patterns and the trimming / AOT guidance.
