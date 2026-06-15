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

## 2. Translate a string

In every form the first argument is a stable **key**; the second is the **in-code default** (the
source-language text, and the terminal fallback); the trailing `(name, value)` pairs fill the ICU
placeholders.

The quickest start needs no receiver at all. Add one `using static` and call `Translate(...)` directly —
the way `using static System.Console;` gives you `WriteLine(...)`. No setup, no DI, no class to wire up:

```csharp
using static ArchPillar.Extensions.Localization.Localizer;

string Greet(string name) =>
    Translate("greeting", "Hello {name}!", ("name", name));

string Inbox(int count) =>
    Translate("inbox", "You have {count, plural, one {# message} other {# messages}}", ("count", count));
```

This goes through the process-wide ambient store (the `IConfiguration` model): reachable from anywhere —
a service, a static helper, even an exception thrown before any container exists. There is nothing to
register and no constructor to thread.

**Calling on a localizer instance.** Once you have an `ILocalizer` receiver — `Localizer.Default`, or
(as the app grows) an injected `ILocalizer<T>` — there are two interchangeable styles: the
`.Translate(...)` method, and a `loc["key", "default"]` indexer that teams coming from `IStringLocalizer`
already know.

```csharp
string a = Localizer.Default.Translate("home.title", "Home");
string b = Localizer.Default["home.title", "Home"];                 // identical lookup, indexer style
string c = Localizer.Default["greeting", "Hello {name}!", [("name", "Ada")]]; // arguments as a (name, value) array
```

> **Pick one convention.** The two are the same call — the analyzer and the extractor recognise both — so
> it is purely your team's taste, but settle on one; mixing `Translate(...)` and `["…"]` only makes call
> sites harder to scan. Note the indexer is an *instance* feature (C# has no static indexers), so the
> receiver-less free-function form above is always the `Translate` method — `Localizer["…"]` does not
> exist, only `Localizer.Default["…"]` on the instance.

> **As your app grows**, scope keys by *category* so two components can both use `"title"` without
> colliding — call `Localizer.For<T>()`, or inject `ILocalizer<T>` (the `ILogger<T>` model). A set of
> shared strings lives in its own scope type. None of that is needed to start; see
> [categories](features.md#categories--the-iloggert-model) and
> [`Localized<TSelf>`](features.md#localizedtself--a-bundle-of-strings).

## 3. Run with the in-code defaults

With no catalogs present, every call renders its in-code default — the app is fully functional in the
source language from day one:

```csharp
Console.WriteLine(Greet("Ada"));   // "Hello Ada!"
Console.WriteLine(Inbox(1));       // "You have 1 message"
Console.WriteLine(Inbox(5));       // "You have 5 messages"
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
# Run from your app folder. Like `dotnet build`, the tool finds the solution (or lone project)
# in the current directory — add German across everything in it that has strings:
dotnet apl add de --output Translations
#   -> Translations/YourApp.de.xliff  (every entry state: NeedsTranslation)
```

`YourApp.de.xliff` lists every key with an empty translation, ready to hand to a translator (next step).
The catalogs are named `{AssemblyName}.{culture}.xliff` so libraries never collide, and the build copies
them beside the binary automatically. (XLIFF is the default; pass `--format arb` or `--format po` to author
in another format — the runtime loads all three.)

> **Pointing it elsewhere:** the cwd default covers the common case; pass a scope only to override it —
> `--solution YourApp.sln` (when the folder has more than one), `--project YourApp.csproj` (add `--recurse`
> to include its project dependencies), or `--input bin/Debug/net10.0`. Run `dotnet apl status` to see which
> assemblies have strings and how many.

> When you reference the package, the build also runs `extract` for you after each real build, so the
> source-language catalog (`{AssemblyName}.en.xliff`) appears in `Translations/` without you asking. It is
> *merged*, not overwritten, so you can keep it in git and even edit the source wording in place (a typo or
> tone fix loads as an override without a recompile); your edits survive the next extract. As code
> changes, `dotnet apl sync --output Translations` reconciles every language file
> (and `--check` is your CI gate). The full lifecycle — including handing files to translators as a zip and
> shipping for production — is in [translation-workflow.md](translation-workflow.md).

## 5. Hand off to translators — and back

A single file you can hand over directly, but the tool gives you a round-trip that also scales once an app
spans several assemblies: `export` bundles every language file into one zip (as XLIFF, the format
translation tools speak), and `import` routes each returned file back to the right catalog by its name.

```bash
# Bundle the 'de' files for the translator:
dotnet apl export --input Translations --lang de --output kit-de.zip
#   -> kit-de.zip  (every {AssemblyName}.de.xliff)

# ...the translator edits the files and sends the zip back...

dotnet apl import --input kit-de.zip --output Translations
#   -> updated Translations/YourApp.de.xliff  (entries they finished are now Translated)
```

The `.xliff` opens in any XLIFF-aware editor — Poedit and Lokalize both show the source and the translation
side by side. `import` writes each file back in the format already on disk (an ARB repo stays ARB); pass
`--format po` on `export` to hand off Portable Object instead. Commit the returned files. The full
lifecycle — scopes, `sync`, deployment — is in [translation-workflow.md](translation-workflow.md).

## 6. Switch culture and see the override

The ambient store loads `Translations/` automatically and resolves against `CurrentUICulture`:

```csharp
using System.Globalization;

CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
Console.WriteLine(Greet("Ada"));   // "Hallo Ada!"
Console.WriteLine(Inbox(5));        // "Sie haben 5 Nachrichten"
```

## 7. (Optional) Wire up dependency injection

DI registration lives in a separate package:

```bash
dotnet add package ArchPillar.Extensions.Localization.DependencyInjection
```

In a host, register the library and inject `ILocalizer<T>`. `UseRequestLocalization` drives the culture in
ASP.NET Core:

```csharp
builder.Services.AddArchPillarLocalization(new LocalizerOptions { SourceCulture = "en" });
```

Using [`Localized<T>`](features.md#localizedtself--a-bundle-of-strings) bundles? Chain the generated
`AddArchPillarLocalizedBundles()` to register every bundle in the assembly, then inject them directly:

```csharp
builder.Services.AddArchPillarLocalization(new LocalizerOptions { SourceCulture = "en" })
                .AddArchPillarLocalizedBundles();
```

DI feeds the **same** ambient store, so injected and receiver-less lookups share one source — inject
`ILocalizer<T>` in a service and call the static `Translate(...)` in an exception path, and both resolve
from the catalogs you registered once. For an isolated environment (test isolation, or hosting more than
one localization scope in a process), construct a `LocalizationContext` directly and thread it through your
own code. See [the localization context](features.md#the-localization-context) for the full model.

Migrating existing `IStringLocalizer` code? Add the `…Localization.StringLocalizer` package and call
`AddArchPillarStringLocalizer` instead — see the migration on-ramp in [features.md](features.md).

## 8. Ship it

In development each library keeps its own `{AssemblyName}.{culture}.xliff` files, which the build copies
beside the binary. On **publish**, the build flattens them into one compact bundle per culture (`de.arb`,
`fr.arb`, …) so production ships a handful of files instead of one per library — automatically, no
configuration. The bundle is ARB by default even when you author in XLIFF: a runtime bundle needs only the
translation, so the most compressible container wins (override with `ArchPillarLocalizationBundleFormat`). The
files bundle works under **every** publish mode, including trimming and NativeAOT, so it is the default
everywhere. To embed catalogs in the assemblies instead — for a single-file or self-contained build — opt into
`ArchPillarLocalizationEmbedTargets=true`; note NativeAOT cannot load culture satellites, so there it is files
or a main-assembly embed. See [translation-workflow.md](translation-workflow.md#deployment) for the details and
the trim/AOT matrix in [recommendations.md](recommendations.md).

## Next

- [translation-workflow.md](translation-workflow.md) — the full lifecycle: scopes, `status`/`extract`/`add`/
  `sync`, the translator handoff (export/import), and deployment.
- [features.md](features.md) — categories, the ambient store, embedding and satellites, formats, ICU
  plurals, DI, and the `IStringLocalizer` migration on-ramp.
- [recommendations.md](recommendations.md) — production patterns and the trimming / AOT guidance.
