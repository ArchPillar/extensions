# 06 — MSBuild Integration & Project Layout

> The `.props`/`.targets` shipped in the NuGet package, the configuration surface, what the build emits, and how target-language files (added on demand, never at build time) reach the runtime. Touches the generator (spec 02), the providers (spec 03), and the runtime (spec 05).

## Purpose

Give the consuming project a small, declarative configuration surface — format, source language, template output location, and the live-extraction opt-in — with sensible defaults the library never forces. The build emits one thing tied to code: the source-language **template**. It does not know which target languages exist; languages are an operations/translation decision made on demand via the tool or Poedit (Decision D-12), never by editing the project or recompiling.

## Configuration surface

All properties use the `ArchPillarLocalization` prefix and are surfaced to the generator via `CompilerVisibleProperty`. Defaults are set in the package `.props` so a project can override them anywhere. **There is deliberately no target-language property** — adding a language is not a build input.

| Property | Default | Purpose |
|---|---|---|
| `ArchPillarLocalizationFormat` | `arb` | template/catalog format: `arb` \| `xliff` \| `po` |
| `ArchPillarLocalizationSourceLanguage` | `en` | the language the in-code defaults are written in (BCP-47) |
| `ArchPillarLocalizationOutputPath` | `$(MSBuildProjectDirectory)\Translations` | directory where the **template** is written and where target files live once added (source tree, version-controlled) |
| `ArchPillarLocalizationEmit` | `true` | master switch for the build-time template extraction (the tool's `extract`); `false` disables it (the generated key registry, the analyzer, and the runtime still work) |
| `ArchPillarLocalizationExtractTransitively` | `false` | extraction runs only where the package is referenced **directly** (the project that authors the strings); set `true` to also extract in a project that references the package transitively or wires the build assets in by hand — see [Reference scope](#reference-scope-direct-vs-transitive) |
| `ArchPillarLocalizationKeyPattern` | *(none)* | optional regular expression enforced by diagnostic `APL0008` (spec 01) |
| `ArchPillarLocalizationCopyTargetsToOutput` | `true` | copy **target** catalogs (not the template) to the application output directory so the runtime can load them |
| `ArchPillarLocalizationEmbedTargets` | `false` | instead of copying, embed target catalogs as assembly resources (single-file deployment); mutually exclusive with copy |
| `ArchPillarLocalizationEmitManifest` | `true` | for a Razor/Blazor project, generate the catalog manifest (`apl-catalogs.json`) and register it as a static web asset so a WebAssembly client can fetch it over HTTP to discover catalogs; inert in non-Razor projects (they load from the file system) |

Example consumer configuration — note there is no language list:

```xml
<PropertyGroup>
  <ArchPillarLocalizationFormat>xliff</ArchPillarLocalizationFormat>
  <ArchPillarLocalizationSourceLanguage>en</ArchPillarLocalizationSourceLanguage>
  <ArchPillarLocalizationOutputPath>$(MSBuildProjectDirectory)\Localization</ArchPillarLocalizationOutputPath>
</PropertyGroup>
```

## Choosing a format

The format is a convenience/authoring choice, not a lock-in — the tool's `convert` (spec 02) moves any template or catalog between all three. The default is **ARB**, because it is the lightest format that maps cleanly to the symbolic-key model (key = JSON key) and is ICU-native and Poedit-readable, with no XML-namespace parsing weight. **XLIFF** is the more *capable* format — it adds a native translation-state machine (initial → needs-review → translated → final) on top of ICU values, which is what professional vendors and translation-management systems expect; choose it when that workflow matters. **Portable Object** is the simplest and most Poedit-traditional, suited to community translators, at the cost of a weaker non-ICU plural model and the `msgctxt`-as-key mapping. Since `convert` is free, picking the default and switching later is cheap.

## What the build emits

On build, two things happen, neither needing a language list. The **generator** (spec 02) emits the strongly-typed key registry as in-assembly source so call sites and the analyzer share rename-safe keys — it writes no files (a generator cannot). The **build's extract target** then runs the tool over the freshly built assembly, reading its **IL** (Decision D-K) and writing the **source-language template** to `OutputPath` in the configured format — the source `.arb` for ARB (default), a source `.xliff` for XLIFF, or a `.pot` for Portable Object — every extracted key, its source text, and metadata (context, comments, references, fingerprint), with no target translations. The build creates no target-language files, requires none to exist, and never edits one. An assembly with no translatable strings yields no template file.

For a host with no readable file system — a Blazor WebAssembly client, which fetches its catalogs over HTTP rather than reading them from disk (spec 05) — the catalogs and a **catalog manifest** (`apl-catalogs.json`, listing every non-source catalog by culture and file name; over HTTP there is no directory to enumerate, so the runtime reads it to discover what to fetch) are served as **static web assets**. A post-build write would land after the pipeline has already resolved and fingerprinted the served assets, so instead this wires **into** the pipeline, the way the SDK contributes its own computed assets (e.g. the JS-initializer-module manifest), via the `DefineStaticWebAssets`/`DefineStaticWebAssetEndpoints` tasks. Catalogs are authored under `OutputPath` (not `wwwroot`), so they are not auto-discovered; they are registered explicitly and split by phase so `AssetKind` alone separates the two layouts (no asset removal):

- **Build** — a target on `ResolveStaticWebAssetsInputsDependsOn` (reached via `PrepareForRun`, late enough that the tool is built and the source template extracted) registers each per-library catalog plus a manifest listing them, as `Build` assets, so `dotnet run` serves the unmerged dev layout.
- **Publish** — a target on `ResolvePublishStaticWebAssetsDependsOn` flattens the catalogs into one bundle per culture (the tool's `merge`) plus a manifest listing the bundles, as `Publish` assets, so the published app ships the compact layout.

Everything is fingerprinted, compressed, and served like any `wwwroot` asset, with nothing committed to the source tree. Emission is on by default (`ArchPillarLocalizationEmitManifest`); these `…DependsOn` properties exist only in Razor/Blazor projects, so this is inert elsewhere — a console or server app loads catalogs from the file system and uses the publish-merge target above instead. Merging catalogs contributed by *referenced* localized libraries into a WebAssembly client is not yet covered by this path (the file-system merge handles it for a server app); the WebAssembly path currently bundles the app's own catalogs.

## Languages are added on demand, not at build

Adding a target language is an operation on the **template**, performed when localization is wanted, by whoever owns it:

- **Via the `dotnet` tool:** `add <lang>` reads the template and writes a new target file (correct per-language header from CLDR, all keys present, targets empty). `sync` later reconciles existing target files against the current template. `convert --to <format>` re-serializes the template or a catalog into another format. (Spec 02.)
- **Via Poedit (Portable Object only):** "create new translation from POT" and "update from POT" perform the equivalent `add`/`sync` natively, so a translator can self-serve without the tool. XLIFF and ARB cannot self-bootstrap a new target language, which is why the tool provides it for them.

The build is never involved in this. Target files appear in `OutputPath` when someone adds them and are kept current by deliberate `sync` (or a translator's Poedit merge), not by recompiling. A team that wants automation may run `dotnet apl sync` in continuous integration — that is an explicit ops choice, not a compile-time coupling.

## Build-time only — no design-time writes

The template is written only on a **real build**, never during a design-time/IDE build. Extraction is the package's `extract` target — an MSBuild `Exec` over the just-built assembly, gated on `'$(DesignTimeBuild)' != 'true' and '$(BuildingProject)' == 'true'` — not a generator output, so editing translatable code in the IDE updates diagnostics and the generated key registry but touches no file on disk until you build. There is no live-extraction option: the tool reads the built assembly, which exists only after a build.

## Reference scope: direct vs transitive

Two of the build assets are packed under `buildTransitive`, so they reach **every** consumer in a dependency graph — a direct reference *and* a project that picks the package up indirectly through another library. That is correct for the **publish-time merge** (`AfterTargets="Publish"`): an app that depends on a localized library three levels down still wants that library's catalogs flattened into the per-culture bundles. But it is *wrong* for **build-time extraction**, which is per-authoring-assembly: a transitive consumer has no strings of its own, so running the tool over its assembly is pure cost (the analyzer, which is `analyzers/`-scoped and therefore direct-only by NuGet default, doesn't even run there).

So the `extract` target self-gates on **whether the package is referenced directly**, using `@(PackageReference->WithMetadataValue('Identity', 'ArchPillar.Extensions.Localization'))` — populated for a direct reference, empty for a transitive one. The merge stays ungated (it self-limits on `Exists($(PublishDir)Translations)`). Net effect:

| | extract (build) | merge (publish) |
|---|---|---|
| **Direct** reference | runs | runs |
| **Transitive** reference | skipped | runs |

**Escape hatch.** A project that authors localized strings but only sees the package transitively — or consumes the build assets by hand (e.g. importing them from `Directory.Build.props`/`.targets`, as this repo's own samples do) — opts back in with:

```xml
<PropertyGroup>
  <ArchPillarLocalizationExtractTransitively>true</ArchPillarLocalizationExtractTransitively>
</PropertyGroup>
```

## Authored / template location vs runtime location

Three roles for `OutputPath` and its files:

1. **Template** — authored by the generator, consumed by the tool and translators. Not loaded at runtime and **not** copied to the app output.
2. **Target catalogs** — created on demand (tool/Poedit) in the same directory, edited by translators, committed to the source tree.
3. **Runtime** — where the application finds the *target* catalogs at run time.

The package `.targets` bridges 2→3. When `ArchPillarLocalizationCopyTargetsToOutput=true` (default), it adds the target files (everything in `OutputPath` except the template) as `Content` with `CopyToOutputDirectory=PreserveNewest`, so they land beside the application binary; the runtime's `LocalizerOptions.TranslationsDirectory` (spec 05) defaults to that copied subdirectory relative to `AppContext.BaseDirectory`. When `ArchPillarLocalizationEmbedTargets=true`, the `.targets` instead adds them as `EmbeddedResource` and the runtime loads them from the assembly manifest (single-file publish). The library supplies the default for both; the consumer may override the runtime directory explicitly.

## Packaging

- Ship `.props` and `.targets` under `buildTransitive/` so configuration and behaviour flow to consuming projects.
- `.props` sets the defaults above and declares the `CompilerVisibleProperty` entries the generator reads.
- `.targets` wires the copy-targets-to-output or embed-targets transform (excluding the template) and runs after the generator in the build graph.
- The analyzer + generator are packaged under `analyzers/dotnet/cs`; the runtime under `lib/`; the `dotnet` tool is shipped as a separate tool package. Referencing the library package activates diagnostics and template emission with zero setup; languages are added later with the tool or Poedit.

## Acceptance criteria

- [ ] A build emits the key registry (in-assembly) and the source-language template (to `OutputPath`, via the extract target); no target-language file is created or required, and the project file contains no language declaration.
- [ ] `ArchPillarLocalizationEmit=false` stops the build-time template extraction while leaving the generated key registry, analyzer diagnostics, and runtime lookup functional.
- [ ] Adding a language with the tool (or Poedit, for Portable Object) creates a target file in `OutputPath` with no build or project edit; a subsequent build does not remove or rewrite it.
- [ ] The template is not copied to the application output; target catalogs are (or are embedded), and the runtime finds them.
- [ ] Changing `ArchPillarLocalizationOutputPath` relocates the template and target files together, and the runtime still finds the targets.
- [ ] With `ArchPillarLocalizationEmbedTargets=true` and a single-file publish, the runtime loads target catalogs from embedded resources with no loose files present.
- [ ] A fresh project that references the package and sets a format produces a template on build; running `tool add de` then yields a working German-capable app after the German file is translated — all without touching the project or rebuilding to "enable" German.
