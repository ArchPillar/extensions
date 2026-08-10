# Localization — tooling and deployment

The `dotnet apl` CLI (the `ArchPillar.Extensions.Localization.Tooling` global tool) turns the
compile-time-extracted source template into per-language catalogs and reconciles them. **You never
hand-author catalogs.**

```bash
dotnet tool install --global ArchPillar.Extensions.Localization.Tooling   # command: dotnet apl
```

## Lifecycle

Run commands from the app folder — like `dotnet build`, the tool finds the solution (or lone
project) in the current directory.

| Command | Purpose |
| --- | --- |
| `dotnet apl status` | Translation coverage — how much of the app is translated (and, before any language exists, which assemblies have strings). `--detail overall\|language\|project\|matrix` chooses how far it aggregates; default `overall` |
| `extract` | Emit the source-language catalog (`{Assembly}.en.xliff`). **Runs automatically after each real build** when the package is referenced |
| `add <culture>` | Create a target file (`{Assembly}.<culture>.xliff`), every entry `NeedsTranslation` |
| `sync` | Reconcile every language file after code changes; **`sync --check` is the CI gate** |
| `export [--lang de] --output kit.zip` | Bundle catalogs for a translator (one zip, or one `<culture>.zip` per language into a folder); the source language is never handed off |
| `import --input kit.zip` | Route returned files back to the right catalog by name |
| `merge --input <dir> --output <dir> [--format aploc\|arb\|xliff\|po]` | Flatten per-library files into one bundle per culture (default format `aploc`); **runs automatically on `dotnet publish`** |
| `convert` | Convert a catalog between formats |

**Scope** defaults to the current directory; override with `--solution App.sln`,
`--project App.csproj` (add `--recurse` for its project dependencies), or `--input bin/Debug/net10.0`.
A project/solution scope reads only the assemblies **those projects build** — not the NuGet packages
and native libraries copied into their `bin`. Which assembly that is comes from an **MSBuild
evaluation** of each project (so a name set in `Directory.Build.props`, built from a property
expression, or renamed by `TargetName` is resolved correctly). A project that will not evaluate is
reported, not guessed around — it is the same project that will not build. `--input`/`--assembly` are
the exception: they read assemblies with no project involved (which is what lets you scan a publish
folder, or a drop from another build).

**Every project in scope is read, not only those referencing this library** — `[DisplayName]`/`[Display]`
are BCL attributes, so a model or contracts project usually references no localizer yet still carries
translatable strings. The call-site pass already skips assemblies with no localizer reference (a
`Translate(...)` call cannot exist without one), so unrelated assemblies cost only a metadata read.
`--no-annotations` when you want call sites only.

**Coverage** (`status`) reports **Translated** (a current translation — `Translated` *or* `Final`),
**Review** (a translation the source drifted under, so it renders but is stale), **Missing**
(untranslated or absent from the catalog), and a floored **%**. The total column names itself:
`Strings` at the `language`/`matrix` levels is a string count; `Units` at `project`/`overall` is
strings × languages, the real work. A project with no language yet shows `—`, not 0%.

> The auto-extracted source catalog is **merged, not overwritten** — keep it in git, and you may
> edit the source wording in place (a typo/tone fix loads as an override **without a recompile**);
> your edits survive the next `extract`.

## Formats

Catalogs round-trip through three bundled **authoring** formats: **XLIFF 2.1** (default), **ARB**
(JSON), and **Portable Object** (`.po`). Author in whichever your pipeline prefers (`--format arb|po`);
the runtime loads all three side by side. When one catalog exists in more than one format the
higher-fidelity file wins (`xliff` > `arb` > `po`, a fixed tie-breaker) and the loser is never loaded.

**APLOC (`.aploc`) is the fourth format — deploy-only, never authored.** It is what the publish-time
merge emits by default: a compact JSON bundle carrying only the translated value per entry, with the
category folded into a **nested object tree** (each dot-segment of the category is one nested object;
a node's keys are plain string members beside its child-namespace objects, and a dotted *key* like
`home.title` stays one member, never split). `@@locale` at the root carries the culture, as in ARB;
the `"@"` apex member exists only for the rare key whose name collides with a child namespace — a
real bundle carries none. It is **lossy by design** — source text, state, comments, references, and
fingerprints are dropped — so do not author it or hand it to a translator; `convert` moves a bundle
back to XLIFF/ARB/PO for editing. In the same-catalog tie-breaker it loses to any authoring format.

Files are named `{AssemblyName}.{culture}.{ext}` so independent libraries never collide, and the
build copies them beside the binary.

**Translator comments.** A note you write beside a string in code — inside the call or attribute
parens, `Translate("k", "d" /* keep under 12 chars */)` — is extracted into the catalog as the
developer comment every format has a slot for: Portable Object `#.`, ARB `description`, XLIFF
`<note>`. So it shows up right next to the string in POEditor, Crowdin, or a plain editor, giving the
translator the context the string alone can't. It is recovered by a source scan (comments can't live
in the built assembly), so it needs the project's source present at extract time; when the source
isn't there (a `/pathmap` CI build), `sync` **keeps** any comment already in the file rather than
dropping it. Comments on the line *above* a call are not extracted — write the note in the parens.

**Source references (opt-in, off by default).** Set
`<ArchPillarLocalizationExtractReferences>true</ArchPillarLocalizationExtractReferences>` in a project
(or pass `--references` to `extract`/`sync`) and entries carry the **files** the string is used in
(gettext `#:`, XLIFF `<note category="reference">`), read from the PDB and recorded project-relative.
Off by default because it binds a git-tracked catalog to where the code lives — move a call and every
language file is rewritten. Files, not lines: line numbers would churn the catalog on any edit that
shifts a line, and a translator has no source tree to look a line up in. Blazor `.razor` components
resolve correctly; MVC/Razor Pages `.cshtml` markup expressions and display annotations have no debug
location and get no reference. No PDB simply means no references, and existing ones are preserved.

## Delivery and deployment

- **Files (default).** Catalogs copy to `Translations/<Assembly>.<culture>.<ext>`; the store reads
  `TranslationsDirectory` on first use. This path works under **every** publish mode, including
  trimming and NativeAOT — the default everywhere.
- **Publish merge.** On `dotnet publish`, per-library files flatten into one compact bundle per
  culture (`de.aploc`, …) automatically (`ArchPillarLocalizationMergeOnPublish`, default on). The
  bundle is APLOC (the deploy-only format above) by default even when you author XLIFF; set
  `ArchPillarLocalizationBundleFormat` to `arb`, `xliff`, or `po` to publish an authoring format
  instead.
- **Embedded (opt-in, `ArchPillarLocalizationEmbedTargets=true`).** Catalogs become standard culture
  **satellite assemblies**, discovered lazily per requested culture. A culture-neutral/merged
  catalog can ride inside the main assembly via `[LocalizationCatalog]`.

> **NativeAOT cannot load culture satellite assemblies** — it degrades to the in-code default.
> For AOT use the files path (default) or a main-assembly embed (`[LocalizationCatalog]`), not
> satellites. See `docs/localization/recommendations.md` for the full trim/AOT matrix.

## Blazor WebAssembly — static web assets, not files

A browser has no file system, so a WebAssembly app's catalogs are delivered as **static web assets**
instead: the build emits a catalog manifest (`apl-catalogs.json`) plus the catalogs, and on publish
the merged per-culture bundles, all through the Razor static-web-asset pipeline
(`ArchPillarLocalizationEmitManifest`, default on). The client fetches them over HTTP — see the
`…Localization.WebAssembly` wiring in `references/di-runtime-and-interop.md`. The beside-the-binary
catalog copy (`ArchPillarLocalizationCopyTargetsToOutput`) is skipped automatically for a WebAssembly
app. Three deployment gotchas:

- **Hosted layouts work out of the box, but only via the asset pipeline.** When an ASP.NET Core
  server references the WebAssembly client with a plain `ProjectReference`, the package registers the
  client's manifest and bundles `AssetMode=All`, so they cross the reference and land at the host's
  `wwwroot/Translations/`; the referenced libraries' raw per-culture catalogs are pruned from the
  publish output (the merged bundle already carries them).
- **The host must serve the catalog content types.** `MapStaticAssets()` honours the content-type
  mappings the package registers (and the fingerprinted routes the manifest's bare file names map
  to); plain `UseStaticFiles()` 404s the unknown catalog extensions, and a manifest that loads while
  every file it lists 404s silently drops the app to its in-code defaults. On a host without the
  asset pipeline, `app.UseArchPillarTranslationFiles()` (in `…Localization.AspNetCore`) registers
  the content types instead.
- **The manifest is mandatory, and its absence fails the build.** Over HTTP there is no directory to
  enumerate, so a WebAssembly app with catalogs but no working `dotnet apl` (which produces the
  manifest) fails with **`APL0100`** — naming how to install the tool — rather than shipping a
  silently un-localized app.

## SDK requirement (the silent gotcha)

The analyzer and generator are built against a modern Roslyn, so the **build** needs **.NET SDK
9.0.3xx+** (any .NET 10 SDK). On an older SDK the package restores and the runtime works, but
extraction and the `APL` diagnostics **silently do nothing** — no template, no warnings. If keys
aren't extracted, check `dotnet --version` first. This is independent of the target framework
(`net8.0` builds fine on a new SDK).
