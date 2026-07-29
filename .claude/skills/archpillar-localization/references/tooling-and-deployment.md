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
| `dotnet apl status` | Which assemblies have strings, and how many |
| `extract` | Emit the source-language catalog (`{Assembly}.en.xliff`). **Runs automatically after each real build** when the package is referenced |
| `add <culture>` | Create a target file (`{Assembly}.<culture>.xliff`), every entry `NeedsTranslation` |
| `sync` | Reconcile every language file after code changes; **`sync --check` is the CI gate** |
| `export [--lang de] --output kit.zip` | Bundle catalogs for a translator (one zip, or one `<culture>.zip` per language into a folder); the source language is never handed off |
| `import --input kit.zip` | Route returned files back to the right catalog by name |
| `merge --input <dir> --output <dir> --format arb` | Flatten per-library files into one bundle per culture; **runs automatically on `dotnet publish`** |
| `convert` | Convert a catalog between formats |

**Scope** defaults to the current directory; override with `--solution App.sln`,
`--project App.csproj` (add `--recurse` for its project dependencies), or `--input bin/Debug/net10.0`.

> The auto-extracted source catalog is **merged, not overwritten** — keep it in git, and you may
> edit the source wording in place (a typo/tone fix loads as an override **without a recompile**);
> your edits survive the next `extract`.

## Formats

Catalogs round-trip through three bundled formats: **XLIFF 2.1** (default), **ARB** (JSON), and
**Portable Object** (`.po`). Author in whichever your pipeline prefers (`--format arb|po`); the
runtime loads all three side by side. When one catalog exists in more than one format the
higher-fidelity file wins (`xliff` > `arb` > `po`, a fixed tie-breaker) and the loser is never loaded.

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

## Delivery and deployment

- **Files (default).** Catalogs copy to `Translations/<Assembly>.<culture>.<ext>`; the store reads
  `TranslationsDirectory` on first use. This path works under **every** publish mode, including
  trimming and NativeAOT — the default everywhere.
- **Publish merge.** On `dotnet publish`, per-library files flatten into one compact bundle per
  culture (`de.arb`, …) automatically (`ArchPillarLocalizationMergeOnPublish`, default on). The
  bundle is ARB by default even when you author XLIFF (override with
  `ArchPillarLocalizationBundleFormat`).
- **Embedded (opt-in, `ArchPillarLocalizationEmbedTargets=true`).** Catalogs become standard culture
  **satellite assemblies**, discovered lazily per requested culture. A culture-neutral/merged
  catalog can ride inside the main assembly via `[LocalizationCatalog]`.

> **NativeAOT cannot load culture satellite assemblies** — it degrades to the in-code default.
> For AOT use the files path (default) or a main-assembly embed (`[LocalizationCatalog]`), not
> satellites. See `docs/localization/recommendations.md` for the full trim/AOT matrix.

## SDK requirement (the silent gotcha)

The analyzer and generator are built against a modern Roslyn, so the **build** needs **.NET SDK
9.0.3xx+** (any .NET 10 SDK). On an older SDK the package restores and the runtime works, but
extraction and the `APL` diagnostics **silently do nothing** — no template, no warnings. If keys
aren't extracted, check `dotnet --version` first. This is independent of the target framework
(`net8.0` builds fine on a new SDK).
