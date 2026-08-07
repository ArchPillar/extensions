# 06 — MSBuild Integration & Project Layout

> The `.props`/`.targets` shipped in the NuGet package, the configuration surface, what the build emits, and how target-language files (added on demand, never at build time) reach the runtime. Touches the generator (spec 02), the providers (spec 03), and the runtime (spec 05).

## Purpose

Give the consuming project a small, declarative configuration surface — format, source language, template output location, and the live-extraction opt-in — with sensible defaults the library never forces. The build emits one thing tied to code: the source-language **template**. It does not know which target languages exist; languages are an operations/translation decision made on demand via the tool or Poedit (Decision D-12), never by editing the project or recompiling.

## Configuration surface

All properties use the `ArchPillarLocalization` prefix and are surfaced to the generator via `CompilerVisibleProperty`. Defaults are set in the package `.props` so a project can override them anywhere. **There is deliberately no target-language property** — adding a language is not a build input.

| Property | Default | Purpose |
|---|---|---|
| `ArchPillarLocalizationFormat` | `xliff` | template/catalog **authoring** format: `arb` \| `xliff` \| `po` |
| `ArchPillarLocalizationBundleFormat` | `aploc` | the **published bundle** format (the publish-time merge output), independent of the authoring format: a runtime bundle needs only the translation, so the most compact container wins — `aploc` (the deploy-only ArchPillar catalog: nested JSON, category folded into a namespace object tree, source/state/comments dropped) \| `arb` (flat JSON) \| `xliff` \| `po` |
| `ArchPillarLocalizationSourceLanguage` | `en` | the language the in-code defaults are written in (BCP-47) |
| `ArchPillarLocalizationCatalogPath` | `$(MSBuildProjectDirectory)\Translations` | directory where the **template** is written and where target files live once added (source tree, version-controlled) |
| `ArchPillarLocalizationEmit` | `true` | master switch for the build-time template extraction (the tool's `extract`); `false` disables it (the generated key registry, the analyzer, and the runtime still work) |
| `ArchPillarLocalizationExtractAnnotations` | `true` | extract display text carried by attributes (`[DisplayName]`, `[Display(Name/Description)]`, `[Description]`, and the `[Localized…]` twins); `false` passes `--no-annotations` to `extract`, leaving only the in-code call sites in the template |
| `ArchPillarLocalizationExtractReferences` | `false` | record which source **files** each string is used in (the gettext `#:` channel), read from the PDB; passes `--references` to `extract`. Off by default: a reference helps a translator but is not part of a string's identity, and it binds a git-tracked catalog to where the code lives — moving a call rewrites the catalog for every language (Decision D-N) |
| `ArchPillarLocalizationExtractTransitively` | `false` | extraction runs only where the package is referenced **directly** (the project that authors the strings); set `true` to also extract in a project that references the package transitively or wires the build assets in by hand — see [Reference scope](#reference-scope-direct-vs-transitive) |
| `ArchPillarLocalizationKeyPattern` | *(none)* | optional regular expression enforced by diagnostic `APL0008` (spec 01) |
| `ArchPillarLocalizationCopyTargetsToOutput` | `true` | copy catalogs (the source catalog and the target catalogs; only the neutral `messages.pot` is held back) to the application output directory so the runtime can load them. **Skipped automatically for a Blazor WebAssembly app** (`UsingMicrosoftNETSdkBlazorWebAssembly`): the browser has no file system, so that app serves catalogs as static web assets instead, and a beside-the-binary copy is dead weight that also leaks to a server host's output root |
| `ArchPillarLocalizationEmbedTargets` | `false` | instead of copying, embed target catalogs as assembly resources (single-file deployment); mutually exclusive with copy |
| `ArchPillarLocalizationEmitManifest` | `true` | for a Razor/Blazor project, generate the catalog manifest (`apl-catalogs.json`) and register it as a static web asset so a WebAssembly client can fetch it over HTTP to discover catalogs; inert in non-Razor projects (they load from the file system) |

Example consumer configuration — note there is no language list:

```xml
<PropertyGroup>
  <ArchPillarLocalizationFormat>xliff</ArchPillarLocalizationFormat>
  <ArchPillarLocalizationSourceLanguage>en</ArchPillarLocalizationSourceLanguage>
  <ArchPillarLocalizationCatalogPath>$(MSBuildProjectDirectory)\Localization</ArchPillarLocalizationCatalogPath>
</PropertyGroup>
```

## Choosing a format

The format is a convenience/authoring choice, not a lock-in — the tool's `convert` (spec 02) moves any template or catalog between all three. The authoring default is **XLIFF**, because source and translation are distinct first-class fields (no source-as-metadata an editor can silently drop), it carries a native translation-state machine (initial → needs-review → translated → final) on top of ICU values, and it opens cleanly in Poedit/Lokalize and the management systems professional vendors expect. **ARB** is the lightest format — it maps cleanly to the symbolic-key model (key = JSON key), is ICU-native and Poedit-readable, with no XML-namespace parsing weight; choose it to author when those properties matter. **Portable Object** is the most Poedit-traditional, suited to community translators, at the cost of a weaker non-ICU plural model and the `msgctxt`-as-key mapping. Since `convert` is free, picking the default and switching later is cheap.

The **published bundle** is a separate choice (`ArchPillarLocalizationBundleFormat`, default **APLOC**) because it has different priorities from an authoring file: the runtime reads only the translation, so the bundle drops the source, state, comments, references and fingerprints entirely and wins on size. **APLOC** (the ArchPillar localization format, `.aploc`) is a deploy-only container built for exactly this: it stays JSON — so the runtime reads it with no new parser and values with newlines/quotes/`=` need no format-specific escaping — but folds the entry's category into a nested object tree, so a namespace segment is written once no matter how many keys hang beneath it, instead of repeated in every flat `Category::Key` member (as ARB must). On a real catalog it is a few times smaller than the flat ARB bundle raw; compressed the two are close (gzip already dedups the repeated prefixes), so APLOC is chosen for the smaller raw payload the WebAssembly client parses and the far more readable pretty form, not a big wire win. Authoring in XLIFF and publishing in APLOC is the default precisely because each step optimizes for a different thing; set the property to `arb`, `xliff`, or `po` to publish one of those instead.

## What the build emits

On build, two things happen, neither needing a language list. The **generator** (spec 02) emits the strongly-typed key registry as in-assembly source so call sites and the analyzer share rename-safe keys — it writes no files (a generator cannot). The **build's extract target** then runs the tool over the freshly built assembly, reading its **IL** (Decision D-K) and writing the **source-language template** to `OutputPath` in the configured format — the source `.arb` for ARB (default), a source `.xliff` for XLIFF, or a `.pot` for Portable Object — every extracted key, its source text, and metadata (comments, references, fingerprint), with no target translations. The build creates no target-language files, requires none to exist, and never edits one. An assembly with no translatable strings yields no template file.

For a host with no readable file system — a Blazor WebAssembly client, which fetches its catalogs over HTTP rather than reading them from disk (spec 05) — the catalogs and a **catalog manifest** (`apl-catalogs.json`, listing every non-source catalog; over HTTP there is no directory to enumerate, so the runtime reads it to discover what to fetch) are served as **static web assets**. A post-build write would land after the pipeline has already resolved and fingerprinted the served assets, so instead this wires **into** the pipeline, the way the SDK contributes its own computed assets (e.g. the JS-initializer-module manifest), via the `DefineStaticWebAssets`/`DefineStaticWebAssetEndpoints` tasks. Catalogs are authored under `OutputPath` (not `wwwroot`), so they are not auto-discovered; they are registered explicitly, with two roles so the app can fold in every referenced library's catalogs (the merge's whole point):

- **Contributor** — a referenced Razor class library registers its own catalogs as static web assets with `AssetMode=All` (so they flow to the consuming app, under `_content/<library>/Translations/`), tagged `ArchPillarLocalizationCatalog` so the app can find them. It emits no manifest. Gated to libraries (`OutputType == Library`).
- **Authority (build)** — the WebAssembly app (`UsingMicrosoftNETSdkBlazorWebAssembly`) gathers its own catalogs (loose files in `OutputPath`) and every referenced library's catalog assets (by trait), collects them, re-homes them under `Translations/` as `Build` assets registered `AssetMode=All`, and emits one manifest listing them all. Reached via `ResolveStaticWebAssetsInputsDependsOn` (during `PrepareForRun`, late enough that referenced assets are resolved and the tool is built).
- **Authority (publish)** — via `ResolvePublishStaticWebAssetsDependsOn`, the same gather, but the tool's `merge` flattens the app's and every library's catalogs into one bundle per culture; the bundles are registered as `Publish` assets, the per-library catalogs are removed from the publish set (`<StaticWebAsset Remove>`), and the manifest lists the bundles.
- **`AssetMode=All`, not `CurrentProject`, for the authority's own re-homed catalogs, bundles, and manifest.** In a hosted layout the project that produces the deployed `wwwroot` is the *server host*, not the WebAssembly app, and the client is reached by a `ProjectReference`. A `CurrentProject` asset does not cross a reference (on SDK versions before one began forwarding a referenced WebAssembly app's own assets), so the manifest and bundles would never arrive — the host would ship the contributor's raw catalogs (which *are* `All` and do propagate) with nothing pointing at them, and the client would fetch a 404 for `apl-catalogs.json` and fall through to its in-code defaults. `All` propagates the same way the contributor's catalogs do, and because the WebAssembly client's base path is the app root (`/`) the bundles and manifest land at the host's `wwwroot/Translations/`, where the client looks. This covers both topologies: a standalone publish keeps working, and a hosting server receives them.
- **Prune (publish, any project)** — the authority's `<StaticWebAsset Remove>` only cleans *its own* resolved set. But a contributor's catalogs are `AssetMode=All`, so they propagate to **every** ancestor: a server host that references the WebAssembly app re-gathers each library's catalogs straight from the library, one reference level past the authority, and they land in the host's `wwwroot/_content/<library>/Translations/` — the authority's removal never reached that far. So a separate prune runs where the published files are actually computed (hooked between `CopyStaticWebAssetsToPublishDirectory` and `_SplitPublishStaticWebAssetsByCopyOptions`, since a removal wired into `ResolvePublishStaticWebAssetsDependsOn` is overwritten when that target re-materializes the referenced assets): it drops every trait-tagged raw catalog from the published set, but only once the authority's manifest is present to supersede them. This runs in the WebAssembly app *and* any host above it, wherever the package's build assets reach via `buildTransitive`.

Everything is fingerprinted, compressed, and served like any `wwwroot` asset, with nothing committed to the source tree. Emission is on by default (`ArchPillarLocalizationEmitManifest`); these `…DependsOn` properties exist only in Razor/Blazor projects, so this is inert elsewhere — a console or server app loads catalogs from the file system and uses the publish-merge target above instead. (A known wart: in the *build* layout the re-homed referenced catalog is also left served at its original `_content/<library>/` path — a harmless duplicate the manifest never points at; the *publish* output is pruned clean, including for a server host.)

Unlike extraction and the file-system merge — best-effort so a missing tool only warns — the manifest is **mandatory** for a WebAssembly authority: with no file system to enumerate, the manifest is the only way the runtime discovers catalogs, so an app that builds without one is silently broken, not merely un-merged. When the authority has catalogs but the tool produced no manifest (missing, unresolvable, or errored), the build fails with **`APL0100`** pointing at how to install `dotnet apl`, rather than shipping an un-localized app. (`APL0100` guards the tool producing the manifest; the `AssetMode=All` registration above is what guarantees it then *reaches* the deployed `wwwroot`.)

The manifest lists catalogs by **bare file name** (`de.arb`), while the served asset is fingerprinted, so the host must serve them through a path that maps the bare name to the fingerprinted file *and* knows the catalog content types (`.arb` is `application/json`, XLIFF is XML, PO is text — the targets register these as `StaticWebAssetContentTypeMapping`). `MapStaticAssets()` (or the `…AspNetCore` package's `UseArchPillarTranslationFiles`) does both; plain `UseStaticFiles()` 404s on the unknown `.arb` type, so a manifest that loads but whose entries 404 is a distinct failure to watch for. The `Localization.WasmHostSample` sample is a **modern hosted layout** (server references the client via the static web asset pipeline, no `Microsoft.AspNetCore.Components.WebAssembly.Server` — the classic package wholesale-copies the client and would mask a propagation gap), published in CI with the deploy shape (build, then publish `--no-build`) and asserted three ways: the published file tree (manifest + bundle present, no `_content` leak, no host-root file copy), the host's `staticwebassets.publish.json` (the `apl-catalogs` asset is present and `AssetMode=All`), and end-to-end (the running host returns `200` for the manifest and for every file it lists).

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

1. **Source catalog** — authored by `extract` (merged into, not overwritten — Decision D-L), consumed by the tool and translators, and editable for source wording. It carries the source language's overrides, so it **is** copied to the app output and loaded at runtime (its un-edited echoes are inert there); only the language-neutral `messages.pot` template is held back.
2. **Target catalogs** — created on demand (tool/Poedit) in the same directory, edited by translators, committed to the source tree.
3. **Runtime** — where the application finds the catalogs (source overrides and target catalogs alike) at run time.

The package `.targets` bridges 1+2→3. When `ArchPillarLocalizationCopyTargetsToOutput=true` (default), it adds the catalog files (everything in `OutputPath` except the neutral `messages.pot`) as `Content` with `CopyToOutputDirectory=PreserveNewest`, so they land beside the application binary; the runtime's `LocalizerOptions.TranslationsDirectory` (spec 05) defaults to that copied subdirectory relative to `AppContext.BaseDirectory`. When `ArchPillarLocalizationEmbedTargets=true`, the `.targets` instead adds them as `EmbeddedResource` and the runtime loads them from the assembly manifest (single-file publish). The library supplies the default for both; the consumer may override the runtime directory explicitly.

## Packaging

- Ship `.props` and `.targets` under `buildTransitive/` so configuration and behaviour flow to consuming projects.
- `.props` sets the defaults above and declares the `CompilerVisibleProperty` entries the generator reads.
- `.targets` wires the copy-to-output or embed transform (excluding only the neutral `messages.pot` template) and runs after the generator in the build graph.
- The analyzer + generator are packaged under `analyzers/dotnet/cs`; the runtime under `lib/`; the `dotnet` tool is shipped as a separate tool package. Referencing the library package activates diagnostics and template emission with zero setup; languages are added later with the tool or Poedit.

## Acceptance criteria

- [ ] A build emits the key registry (in-assembly) and the source-language template (to `OutputPath`, via the extract target); no target-language file is created or required, and the project file contains no language declaration.
- [ ] `ArchPillarLocalizationEmit=false` stops the build-time template extraction while leaving the generated key registry, analyzer diagnostics, and runtime lookup functional.
- [ ] Adding a language with the tool (or Poedit, for Portable Object) creates a target file in `OutputPath` with no build or project edit; a subsequent build does not remove or rewrite it, and a re-extract merges into (does not overwrite) the source catalog, preserving any edited source wording.
- [ ] The source catalog and target catalogs are copied to the application output (or embedded) and the runtime finds them; only the language-neutral `messages.pot` is held back.
- [ ] Changing `ArchPillarLocalizationCatalogPath` relocates the source and target files together, and the runtime still finds them.
- [ ] With `ArchPillarLocalizationEmbedTargets=true` and a single-file publish, the runtime loads target catalogs from embedded resources with no loose files present.
- [ ] A fresh project that references the package and sets a format produces a template on build; running `tool add de` then yields a working German-capable app after the German file is translated — all without touching the project or rebuilding to "enable" German.
- [ ] Publishing a **server host that references a WebAssembly app** (which references a localized library), in the modern layout (composition via the static web asset pipeline, no `Microsoft.AspNetCore.Components.WebAssembly.Server`) and via the deploy shape (build, then `publish --no-build`), ships the merged per-culture bundle(s) and `apl-catalogs.json` under `wwwroot/Translations`, and does **not** leave the library's raw per-culture catalogs under `wwwroot/_content/<library>/Translations/` nor any beside-the-binary catalog copy at the host output root.
- [ ] The authority registers its bundles and manifest `AssetMode=All`, so they cross the `ProjectReference` and appear in the *host's* `staticwebassets.publish.json`; running the host, `GET /Translations/apl-catalogs.json` and every file it lists return `200`, and the merged bundle contains the referenced library's strings as well as the app's own.
- [ ] A WebAssembly authority with catalogs but no working `dotnet apl` fails the build with `APL0100` (naming how to install the tool) instead of producing a manifest-less, silently un-localized app.
