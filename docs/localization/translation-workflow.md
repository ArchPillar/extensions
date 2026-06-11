# Translation workflow

The end-to-end lifecycle of a translatable string, from a developer typing it to a localized app in
production — and the `dotnet apl` commands that drive each stage across a whole solution, not one assembly at
a time.

```
 author (code)  ──build──▶  extract  ──▶  add <lang>  ──▶  export (zip)  ──▶  translator
      ▲                       │                                                    │
      │                       │                                              import (zip)
   sync ◀───────────── code changes                                               │
      │                                                                            ▼
      └──────────────────────────  commit {AssemblyName}.{culture}.arb  ◀──────────
                                              │
                                          publish ──▶  merge: one bundle per culture
```

Install the tool once (it is a .NET global tool; the command is `dotnet apl`):

```bash
dotnet tool install --global ArchPillar.Extensions.Localization.Tooling
```

> **Try it against a sample.** The localization samples that import `Localization.Authoring.props` run the
> generator on build, so you can exercise the whole flow against a real assembly from this repo:
> ```bash
> dotnet build samples/Localization/Localization.ConsoleSample
> dotnet apl extract --project samples/Localization/Localization.ConsoleSample/Localization.ConsoleSample.csproj --output /tmp/x
> dotnet apl add de --project samples/Localization/Localization.ConsoleSample/Localization.ConsoleSample.csproj --output /tmp/x
> ```
> (The samples don't *ship* generated catalogs — they demonstrate the runtime — but they let you test
> extraction end to end. A consuming app gets the generator from the NuGet package automatically.)

## Scope: a whole app at once

Every authoring command (`status`, `extract`, `add`, `sync`) takes a **scope** instead of a single
assembly, and fans out over every in-scope assembly that actually has strings:

| Scope | Meaning |
|---|---|
| `--solution App.sln` | every project in the solution (`.sln` or `.slnx`) |
| `--project App.csproj` | one project; add `--recurse` to follow its project references |
| `--input <dir>` | scan a build-output folder (e.g. `bin/Debug/net10.0` or a publish dir) for assemblies |
| `--assembly <dll>` | a single assembly (the low-level form) |

With **no scope at all**, the tool defaults to the current directory like `dotnet build` — a lone solution
wins, else a lone project. So from your app's folder you can just run `dotnet apl add de --output
Translations`. `--project` and `--solution` also accept a **folder** or no value, finding the single file in
that folder (or the current directory). An ambiguous folder (more than one project/solution) is an error
rather than a guess.

The tool reads the template the generator **bakes into each built assembly**, so the assemblies must be
built first. It reads them from metadata without loading code, so pointing `--input` at a large output tree
is safe.

## 1. Discover — which assemblies have strings?

```bash
dotnet apl status --solution App.sln
# App.Web   (source en)  42 string(s)
# App.Core  (source en)  17 string(s)
# 2 assembly(ies), 59 string(s) total.
```

Add `--output Translations` to also report per-language progress (`de: 31/42 translated`).

## 2. Extract — the source template

On a real build the package's MSBuild target runs `extract` for you, writing
`{AssemblyName}.{SourceLanguage}.arb` (e.g. `App.Web.en.arb`) into your `Translations/` folder. To run it
by hand over a scope:

```bash
dotnet apl extract --solution App.sln --output Translations
```

This template is the source side; you do not translate it directly and it is not shipped.

## 3. Add a language

```bash
dotnet apl add de --solution App.sln --output Translations
# -> Translations/App.Web.de.arb, Translations/App.Core.de.arb  (all NeedsTranslation)
```

`add` creates the language for **every** assembly that has strings and **skips** any that already have it
(so it never resets existing translations — use `sync` to update those, or `--force` to recreate).

## 4. Hand off to translators — and back

Bundle the per-assembly catalogs for a language into a single zip, converted to XLIFF (the format most
translation tools speak):

```bash
dotnet apl export --input Translations --lang de --output kit-de.zip
#   kit-de.zip:  App.Web.de.xliff, App.Core.de.xliff
```

Send `kit-de.zip`. When it comes back translated, import it — each file is routed back to its origin
assembly's catalog by its name:

```bash
dotnet apl import --input kit-de.zip --output Translations
#   -> updated Translations/App.Web.de.arb, Translations/App.Core.de.arb
```

Use `--format po` to hand off Portable Object instead of XLIFF.

## 5. Sync — keep catalogs current as code changes

When source strings are added, edited, or removed, reconcile every language file against the freshly
extracted templates:

```bash
dotnet build
dotnet apl sync --solution App.sln --output Translations
```

New keys arrive as `NeedsTranslation`; an edited source flips its entry to `NeedsReview` (the old
translation is kept, not lost); a removed key is dropped. In CI, make it a gate — exit 0 when in sync, 1 on
drift:

```bash
dotnet apl sync --solution App.sln --output Translations --check
```

## Deployment

In development each library owns its `{AssemblyName}.{culture}.arb` files. For production you have three
paths; the build wires the first two automatically.

- **Files (default).** The build copies each library's catalogs beside the binary, then on **publish**
  flattens them into **one bundle per culture** (`de.arb`, `fr.arb`, …) via `dotnet apl merge`, so a 300-
  library app ships a few files, not hundreds. The runtime loads them identically. Works under every publish
  mode, including trimming and NativeAOT — this is the recommended path.
- **Embedded / satellites (opt-in, `ArchPillarLocalizationEmbedTargets=true`).** Catalogs become per-culture
  satellite assemblies (or ride in the main assembly), for single-file / self-contained publish.
- **Manual merge.** Run it yourself for a custom pipeline:

  ```bash
  dotnet apl merge --input <published Translations> --output <bundle dir> --source en
  ```

The merge skips the source language and untranslated entries — it produces the **runtime** bundle, not a
translator file. For the trim / single-file / NativeAOT support matrix, see
[recommendations.md](recommendations.md).

## Naming convention

Dev/source catalogs are `{AssemblyName}.{culture}.{ext}` (e.g. `App.Web.de.arb`). The assembly prefix keeps
two libraries' `de` catalogs from colliding and lets `import` route a returned translation back to its
origin. The runtime reads each catalog's culture from its **content** (`@@locale`), not its file name, so
the prefix never affects resolution. Production bundles (after merge) are bare `{culture}.{ext}`.
