# ArchPillar.Extensions.Localization

A user-interface translation library for .NET. Write each translatable string once, at the call
site, as an in-code default in ICU MessageFormat; a Roslyn generator extracts it at compile time for
translators; at runtime, translated catalogs (ARB, XLIFF 2.1, or Portable Object) load as pluggable
overrides. The in-code default is always the source of truth and the terminal fallback, so an app
with no translation files still runs correctly.

## Why?

The usual `.resx` + `IStringLocalizer` flow inverts the source of truth: the key is a lookup into a
resource file, and the call site says nothing about what the string actually is. Strings drift from
their translations, a missing resource yields the bare key, and there is no compile-time check that a
string was extracted or that its placeholders line up.

This library puts the **source string at the call site** and makes the build do the bookkeeping:

- The English (or whatever `SourceCulture` you choose) default lives in code and renders even with no
  catalogs loaded — there is no "missing resource" state, only "no override yet".
- A source generator extracts every call site into a template, and an analyzer flags a non-constant
  key, invalid ICU, or a placeholder with no argument **before** you ship.
- Translations are ordinary files a translator edits in standard formats; at runtime they layer over
  the defaults in one ambient store that needs no DI — so even an exception message thrown deep in a
  library, before any container exists, localizes.

## Quick Start

```csharp
using static ArchPillar.Extensions.Localization.Localizer;
using System.Globalization;

// Translate anywhere with a using static, the way `using static System.Console;` gives you WriteLine.
// "greeting" is the key; "Hello {name}!" is the in-code default (the source text and the fallback).
string Greet(string name) => Translate("greeting", "Hello {name}!", ("name", name));

Console.WriteLine(Greet("Ada"));                       // "Hello Ada!" (the in-code default)

CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
Console.WriteLine(Greet("Ada"));                       // "Hallo Ada!" once a de catalog is loaded
```

`Translate` goes through the process-wide ambient store, reachable with no services (or call
`Localizer.Default.Translate(...)` without the `using static`). Translations come from a
`Translations/de.arb` file beside the binary (loaded automatically), an embedded catalog, or
`Localizer.AddCatalog(...)`. Scope keys by category with `ILocalizer<T>` as an app grows — see
[getting-started.md](getting-started.md).

## Features

| Feature | Description |
|---------|-------------|
| In-code default as source of truth | The call-site default renders for the source language and as the terminal fallback for every other. |
| Compile-time extraction | A Roslyn generator extracts attributed call sites into a source template; an analyzer surfaces `APL` diagnostics in the editor. |
| Categories (the `ILogger<T>` model) | `ILocalizer<T>` scopes keys by `typeof(T)`'s full name; no user-managed namespaces. |
| The ambient store | One process-wide, layered, DI-free store (`IConfiguration` model) reachable from anywhere, including exception text. |
| Instantiable context | `LocalizationContext` is the same environment as an object — construct, configure, dispose; build one directly for an isolated, static-free setup. |
| Files / embedded / satellites | Loose files by default (trim/AOT-safe); opt-in embedding routes catalogs into culture satellite assemblies. |
| ICU MessageFormat | Arguments, `plural` / `selectordinal` / `select`, embedded CLDR plural data. |
| Standard formats | ARB (default), XLIFF 2.1, and Portable Object — round-tripped by the bundled providers. |
| Dependency injection | `AddArchPillarLocalization` feeds the process-wide ambient context and registers injectable native localizers; the generated `AddArchPillarLocalizedBundles()` registers the assembly's `Localized<T>` bundles. |
| `IStringLocalizer` interop + migration | A separate `…StringLocalizer` package (`AddArchPillarStringLocalizer`): a composing adapter, on-by-default extraction of indexer literals, and a no-op `L(...)` marker — droppable once migration is done. |
| Display annotations | `[DisplayName]` / `[Display]` / `[Description]` extracted by default (opt-out, text-as-key); optional `[Localized…]` twins supply the source default when you key by a string id; `GetLocalizedDisplayName()` localizes enums; the `…AspNetCore` package routes MVC DataAnnotations through the localizer. |
| Blazor WebAssembly / HTTP loading | Where there is no file system, `AddCatalogsFromManifestAsync` fetches catalogs over HTTP, discovering them via a build-emitted manifest; the separate `…AspNetCore` package serves the catalog formats as static files (`UseArchPillarTranslationFiles`). |
| Publishing | A publish-time merge to one bundle per culture; a documented trim / single-file / AOT matrix. |
| Zero external dependencies | The runtime, formats, and ICU parser use only the BCL — no third-party packages. |

> **No external dependencies.** The parsers and format providers are hand-rolled on the BCL rather than
> taken from existing libraries. For why — and how the maturity gap is closed — see
> [Why no external dependencies](internals/README.md#why-no-external-dependencies).

## Performance

The runtime lookup is on the UI hot path: a **static label** (a literal message with no arguments)
resolves with **zero allocations**, pinned by allocation tests, and tracked by
`benchmarks/Localization.Benchmarks` (`dotnet run -c Release --project benchmarks/Localization.Benchmarks`).

## Documentation

- [getting-started.md](getting-started.md) — install to first translated string, step by step.
- [translation-workflow.md](translation-workflow.md) — the full authoring → translate → ship lifecycle and the `dotnet apl` commands.
- [features.md](features.md) — every feature, with examples.
- [recommendations.md](recommendations.md) — production patterns, ordering constraints, and the
  trim / AOT guidance.
- [internals/SPEC.md](internals/SPEC.md) — the design contract (Goals / Non-Goals / concepts), plus
  the detailed design specs and decision records under [internals/](internals/).

## Samples

Runnable examples under [`samples/Localization/`](../../samples/Localization/):

- [Localization.ConsoleSample](../../samples/Localization/Localization.ConsoleSample/) — a generic
  host resolving `ILocalizer` from DI: named arguments, ICU plurals, an English default, a German override.
- [Localization.AspNetSample](../../samples/Localization/Localization.AspNetSample/) — a Minimal API
  with `UseRequestLocalization`; endpoints inject the native localizer and the `IStringLocalizer<T>` adapter.
- [Localization.BlazorSample](../../samples/Localization/Localization.BlazorSample/) — a server-rendered
  Razor component injecting both, with culture-switch links.
- [Localization.WasmSample](../../samples/Localization/Localization.WasmSample/) +
  [Localization.WasmLibrary](../../samples/Localization/Localization.WasmLibrary/) — Blazor WebAssembly: catalogs
  are discovered through a build-emitted manifest and fetched over HTTP (`AddCatalogsFromManifestAsync`); the
  build gathers the app's own and the referenced library's catalogs (merged into one bundle per culture on
  publish), and culture switches in-process.
- [Localization.TodoSample](../../samples/Localization/Localization.TodoSample/) — a console to-do app
  with English + German/French and a pseudo-localization smoke test.
- [Localization.GreetingLibrary](../../samples/Localization/Localization.GreetingLibrary/) +
  [Localization.LibraryConsumer](../../samples/Localization/Localization.LibraryConsumer/) — a no-DI,
  batteries-included library that ships German embedded as a satellite, consumed with zero configuration,
  including a localized exception message thrown with no services.
- [Localization.MigrationSample](../../samples/Localization/Localization.MigrationSample/) — migrating
  an existing `AddLocalization()` + `.resx` app: the composing adapter keeps the legacy `.resx` resolving
  while ArchPillar wins where it has an entry, plus an `L(...)` marker.
- [Localization.AotSample](../../samples/Localization/Localization.AotSample/) — a NativeAOT app
  localizing the AOT-safe way (a loose file plus a main-assembly embedded catalog, no satellite).
- [Localization.TrimSample](../../samples/Localization/Localization.TrimSample/) — validates the
  embedded/satellite path under trimming / single-file / AOT (see [recommendations.md](recommendations.md)).
