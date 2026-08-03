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
      └──────────────────────────  commit {AssemblyName}.{culture}.xliff  ◀────────
                                              │
                                          publish ──▶  merge: one bundle per culture
```

Install the tool once (it is a .NET global tool; the command is `dotnet apl`):

```bash
dotnet tool install --global ArchPillar.Extensions.Localization.Tooling
```

> **Try it against a sample.** The localization samples are wired (via the repo's `Directory.Build` files)
> to run the generator and the build-time extract, so you can exercise the whole flow against a real
> assembly from this repo:
> ```bash
> dotnet build samples/Localization/Localization.ConsoleSample
> dotnet apl extract --project samples/Localization/Localization.ConsoleSample/Localization.ConsoleSample.csproj --output /tmp/x
> dotnet apl add de --project samples/Localization/Localization.ConsoleSample/Localization.ConsoleSample.csproj --output /tmp/x
> ```
> Every sample commits its catalogs — the generated `{AssemblyName}.en.xliff` source catalog and the
> `de`/`fr` translations — so they double as a worked example of the git-tracked source workflow, and you
> can still re-run extraction against them as above. (A consuming app gets the generator from the NuGet
> package automatically.)

## Scope: a whole app at once

Every authoring command (`status`, `extract`, `add`, `sync`) takes a **scope** instead of a single
assembly, and fans out over every in-scope assembly that actually has strings:

| Scope | Meaning |
|---|---|
| `--solution App.sln` | every project in the solution (`.sln` or `.slnx`) |
| `--project App.csproj` | one project; add `--recurse` to follow its project references |
| `--input <dir>` | scan a build-output folder (e.g. `bin/Debug/net10.0` or a publish dir) for assemblies |
| `--assembly <dll>` | a single assembly (the low-level form) |

A project or solution scope reads **only the assemblies those projects build** — never the packages copied
beside them. A `bin` folder is mostly other people's code (every NuGet dependency and native interop library
lands there), and none of it is yours to translate. To cover the libraries an app pulls in, add `--recurse`,
which follows *project* references. `--input` is the deliberate exception: it names a directory of built
assemblies, so everything under it is in scope — that is what makes scanning a publish folder possible.

Which assembly a project builds is **asked of MSBuild**, not guessed from the project file: the name can come
from `Directory.Build.props`, a property expression (`$(MSBuildProjectName).Core`), a conditioned property
group, or an import, and `TargetName`/`TargetExt` can rename the file after that — none of it visible in the
XML. The tool evaluates every in-scope project in **one** MSBuild process (an evaluation, not a build), because
starting MSBuild costs more than evaluating with it — this repository's 57 projects evaluate in about 2.5s that
way, against about 17s one process at a time.

A project MSBuild cannot evaluate is **reported, not guessed around**: it is the same project that will not
build, so it has no assembly to scan, and inventing a name for it would report "no strings" for a project that
may be full of them. To read built assemblies with no usable project — a publish folder, a drop from another
build — use `--input <dir>` or `--assembly <dll>`, which never look at a project at all.

**Every project in scope is read, not only those referencing this library.** That looks like a missing
optimisation and is not: `[DisplayName]` and `[Display]` are Base Class Library attributes, so a contracts or
model project whose strings are pure DataAnnotations usually references no localizer at all — and its strings
are still yours to translate. Filtering the scope by "references the localization package" would drop them
silently. The saving you would want from such a filter is already taken where it is safe: the **call-site**
pass skips any assembly that references no localizer, since a `Translate(...)` call cannot exist without one,
so an unrelated assembly costs only a metadata read. Pass `--no-annotations`
(`ArchPillarLocalizationExtractAnnotations=false`) when you want call sites only.

With **no scope at all**, the tool defaults to the current directory like `dotnet build` — a lone solution
wins, else a lone project. So from your app's folder you can just run `dotnet apl add de`.
`--project` and `--solution` also accept a **folder** or no value, finding the single file in
that folder (or the current directory). An ambiguous folder (more than one project/solution) is an error
rather than a guess.

The catalog commands (`export`, `import`, `merge`, `manifest`) take the **same scope** — the only difference
is what they read off each in-scope project: its `Translations` folder rather than its built assembly. So
`export --solution App.sln` gathers every project's catalogs and a bare `export` uses the current
project/solution's `Translations`, exactly as `add`/`sync` do. For these commands `--input <dir>` names an
explicit catalog folder (their low-level form, in place of `--assembly`).

The tool **extracts the strings from each built assembly's IL** (Decision D-K — reading compiled metadata,
never source, so it also catches strings in Razor/Blazor/MVC generated code), so the assemblies must be
built first. It reads them without loading code, so pointing `--input` at a large output tree is safe. An
assembly with no translatable strings produces no file — empty templates are never written.

## 1. Discover — which assemblies have strings?

```bash
dotnet apl status --solution App.sln
```

Before any language exists there is nothing to measure, so `status` simply lists what there is to translate —
one row per assembly with its string count. Pass `--catalog-path <folder>` if your catalogs do not live in the
default `Translations`.

Once catalogs exist the same command reports **coverage**, and `--detail` chooses how far it is aggregated.
See [Tracking coverage](#tracking-coverage) below.

## 2. Extract — the source template

On a real build the package's MSBuild target runs `extract` for you, **merging** the freshly extracted strings
into `{AssemblyName}.{SourceLanguage}.xliff` (e.g. `App.Web.en.xliff`) in your `Translations/` folder — it no longer
overwrites the file, so the source catalog is a stable artifact you keep in git and whose wording history is
tracked over time. To run it by hand over a scope:

```bash
dotnet apl extract --solution App.sln
```

Each project's catalogs land in its own `Translations/` folder. `--catalog-path <folder>` renames that
folder; `--output <dir>` overrides it entirely, gathering every catalog into one directory (relative to the
current directory, like the `dotnet` CLI's own `--output`).

This is the source side. You usually leave it alone — the in-code default is the terminal fallback, so an
un-edited source entry (an *echo* of the default) is inert and ships nothing. But the source language **is**
editable: change an entry's wording here to fix a typo or adjust tone without a recompile, and it becomes a
source *override* that loads above the in-code default and is shipped like any translation. A re-extract
preserves your edits (and flags one for review if the in-code default later drifts under it).

## 3. Add a language

```bash
dotnet apl add de --solution App.sln
# -> Translations/App.Web.de.xliff, Translations/App.Core.de.xliff  (all NeedsTranslation)
```

`add` creates the language for **every** assembly that has strings and **skips** any that already have it
(so it never resets existing translations — use `sync` to update those, or `--force` to recreate).

## 4. Hand off to translators — and back

`export` bundles the per-assembly catalogs into a zip, converted to XLIFF (the format most translation tools
speak). It takes the same scope as the authoring commands, so a `--solution`/`--project`/cwd run gathers every
in-scope `Translations` folder:

```bash
dotnet apl export --solution App.sln --lang de --output kit-de.zip
#   kit-de.zip:  App.Web.de.xliff, App.Core.de.xliff
```

`--lang` is an optional filter. Omit it and `--output` becomes a **directory** with one `<culture>.zip` per
target language (the source language is never handed off):

```bash
dotnet apl export --solution App.sln --output ./kits
#   ./kits/de.zip, ./kits/fr.zip, …
```

Send the zip. When it comes back translated, import it — each file is routed back to its origin assembly's
catalog by its name, into each project's `Translations` folder (or an explicit `--catalog-path`/`--output`):

```bash
dotnet apl import --input kit-de.zip --solution App.sln
#   -> updated Translations/App.Web.de.xliff, Translations/App.Core.de.xliff
```

`import` writes each returned catalog in the format already on disk for that assembly (so a repo authored
in ARB stays ARB); a catalog with no existing file lands in the authoring default (XLIFF). Use `--format po`
on `export` to hand off Portable Object instead of XLIFF.

### Give translators context — a note beside the string

A string on its own is often ambiguous ("Post" the verb or the noun? how long can this label be?). Add a
note for the translator by writing a comment **inside the call's parentheses**, right next to the string:

```csharp
loc.Translate("post.submit", "Post" /* the submit button, a verb */);
loc.Translate("home.title", "Home" /* shown in the top nav; keep under 12 chars */);

// It works on display annotations too:
[Display(Name = "Active" /* the running state, not the verb */)]
public Status Status { get; set; }
```

Extraction carries the note into every catalog as the format's translator comment — XLIFF `<note>`, ARB
`description`, Portable Object `#.` — so it appears right beside the string in POEditor, Crowdin, or a
plain editor. Two things to know:

- **Write it in the parens, not on the line above.** A comment on the preceding line is ordinary code
  commentary and is not extracted; only a comment inside the argument list is a translator note.
- **It lives only in source, and is never wiped.** The note is recovered by scanning your source at extract
  time (comments can't be read from a compiled assembly), so it never ships in your binary. If a build can't
  see the source — a deterministic `/pathmap` CI build, for instance — `sync` simply **keeps** the note
  already in the catalog rather than dropping it.

## 5. Sync — keep catalogs current as code changes

When source strings are added, edited, or removed, reconcile every language file against the freshly
extracted templates:

```bash
dotnet build
dotnet apl sync --solution App.sln
```

New keys arrive as `NeedsTranslation`; an edited source flips its entry to `NeedsReview` (the old
translation is kept, not lost); a removed key is dropped. In CI, make it a gate — exit 0 when in sync, 1 on
drift:

```bash
dotnet apl sync --solution App.sln --check
```

## Tracking coverage

`status` answers "how much of the app is translated?". Every project's catalogs are measured against that
project's extracted template, and `--detail` chooses only how far the result is aggregated before it is shown:

```bash
dotnet apl status --solution App.sln                     # one line for the whole app (the default)
dotnet apl status --solution App.sln --detail language   # one row per language
dotnet apl status --solution App.sln --detail project    # one row per project
dotnet apl status --solution App.sln --detail matrix     # every project × language pair
```

Every level reports the same four numbers, so they always reconcile:

| Column | Meaning |
|---|---|
| **Translated** | a current translation exists (`Translated` **or** `Final` — a reviewed string is done) |
| **Review** | a translation exists but the source drifted under it (`NeedsReview`) — it renders, but it is stale |
| **Missing** | untranslated, **or** absent from the catalog entirely (a catalog not synced since keys were added never reads as complete) |
| **%** | `Translated / total`, floored — so one string short of complete never shows 100% |

The totals differ by level, and the column name says which: `Strings` at the `language` and `matrix` levels is
a real string count, while `Units` at the `project` and `overall` levels is strings × languages — the actual
work, since each string must be translated once per language. A project that has no language yet shows its
string count and `—` for coverage: it is not 0% translated, it simply is not being translated.

```
╭──────────┬───────────┬───────┬────────────┬────────┬─────────┬─────╮
│ Projects │ Languages │ Units │ Translated │ Review │ Missing │   % │
├──────────┼───────────┼───────┼────────────┼────────┼─────────┼─────┤
│ 2        │ 3         │   177 │        140 │      9 │      28 │ 79% │
╰──────────┴───────────┴───────┴────────────┴────────┴─────────┴─────╯
```

## Deployment

In development each library owns its `{AssemblyName}.{culture}.xliff` files. For production you have three
paths; the build wires the first two automatically.

- **Files (default).** The build copies each library's catalogs beside the binary, then on **publish**
  flattens them into **one bundle per culture** (`de.arb`, `fr.arb`, …) via `dotnet apl merge`, so a 300-
  library app ships a few files, not hundreds. The bundle is ARB regardless of the authoring format
  (`ArchPillarLocalizationBundleFormat`, default `arb`): a runtime bundle reads only the translation, so the
  most compressible container wins — a minified ARB bundle gzips to roughly 60% of the XLIFF equivalent, which
  still carries the now-redundant source. The runtime loads them identically. Works under every publish mode,
  including trimming and NativeAOT — this is the recommended path.
- **Embedded / satellites (opt-in, `ArchPillarLocalizationEmbedTargets=true`).** Catalogs become per-culture
  satellite assemblies (or ride in the main assembly), for single-file / self-contained publish.
- **Manual merge.** Run it yourself for a custom pipeline:

  ```bash
  dotnet apl merge --input <published Translations> --output <bundle dir> --source en
  ```

The merge skips untranslated entries and includes any genuine source-language overrides (a source language with
no edits contributes no bundle) — it produces the **runtime** bundle, not a translator file. For the trim /
single-file / NativeAOT support matrix, see [recommendations.md](recommendations.md).

## Naming convention

These names are a **convention, not a rule** — the runtime reads each catalog's culture from its **content**
(`@@locale`), never its file name, so any name resolves. The convention exists because the names are the one
thing the *tooling* keys on:

- **Per-assembly dev/source catalogs** are `{AssemblyName}.{culture}.{ext}` (e.g. `App.Web.de.xliff`). The
  assembly prefix keeps two libraries' `de` catalogs from colliding in one folder, and lets `import` route a
  returned translation back to its origin assembly by file name. The authoring commands write this shape, and
  the samples follow it.
- **Bundled / published catalogs** are bare `{culture}.{ext}` (e.g. `de.arb`) — one per culture after `merge`.
  At that point the per-assembly identity has been flattened away, so the simple name is the convention.

Because resolution is content-based, you can deviate (a hand-authored `de.arb` dropped in a folder still
loads); the convention just keeps the tool-driven workflow unambiguous.
