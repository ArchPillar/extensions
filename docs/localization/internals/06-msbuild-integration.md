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
| `ArchPillarLocalizationOutputPath` | `$(MSBuildProjectDirectory)\Translations` | directory where the **template** is authored and where target files live once added (source tree, version-controlled) |
| `ArchPillarLocalizationLiveExtraction` | `false` | when `true`, the generator also rewrites the template during design-time/IDE builds (live extraction while editing); when `false`, only on a real build |
| `ArchPillarLocalizationEmit` | `true` | master switch for template emission; `false` disables it (the analyzer and runtime still work) |
| `ArchPillarLocalizationKeyPattern` | *(none)* | optional regular expression enforced by diagnostic `APL0008` (spec 01) |
| `ArchPillarLocalizationCopyTargetsToOutput` | `true` | copy **target** catalogs (not the template) to the application output directory so the runtime can load them |
| `ArchPillarLocalizationEmbedTargets` | `false` | instead of copying, embed target catalogs as assembly resources (single-file deployment); mutually exclusive with copy |

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

On build, the generator (spec 02) emits exactly one artifact tied to code: the **source-language template** in `OutputPath`, in the configured format — the source `.arb` for ARB (default), a source `.xliff` for XLIFF, or a `.pot` for Portable Object. The template carries every extracted key, its source text, and metadata (context, comments, references, fingerprint), with no target translations. That is the generator's whole filesystem responsibility. It creates no target-language files, requires none to exist, and never edits one.

## Languages are added on demand, not at build

Adding a target language is an operation on the **template**, performed when localization is wanted, by whoever owns it:

- **Via the `dotnet` tool:** `add <lang>` reads the template and writes a new target file (correct per-language header from CLDR, all keys present, targets empty). `sync` later reconciles existing target files against the current template. `convert --to <format>` re-serializes the template or a catalog into another format. (Spec 02.)
- **Via Poedit (Portable Object only):** "create new translation from POT" and "update from POT" perform the equivalent `add`/`sync` natively, so a translator can self-serve without the tool. XLIFF and ARB cannot self-bootstrap a new target language, which is why the tool provides it for them.

The build is never involved in this. Target files appear in `OutputPath` when someone adds them and are kept current by deliberate `sync` (or a translator's Poedit merge), not by recompiling. A team that wants automation may run `dotnet apl sync` in continuous integration — that is an explicit ops choice, not a compile-time coupling.

## Live extraction (the opt-in)

Default (`LiveExtraction=false`): the generator computes in the IDE (for diagnostics and code generation) but rewrites the template only on a real build — matching the C# documentation XML file. The `DesignTimeBuild` MSBuild property, surfaced via `CompilerVisibleProperty`, is what the generator reads to suppress the design-time write.

Opt-in (`LiveExtraction=true`): the generator also rewrites the template during design-time/IDE builds, so it tracks the code live as the developer edits translatable strings. Write-if-changed (spec 02) keeps it from thrashing. Useful when a developer is iterating on source strings; unnecessary otherwise.

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

- [ ] A build emits only the source-language template; no target-language file is created or required, and the project file contains no language declaration.
- [ ] With `LiveExtraction=false`, editing translatable code in the IDE does not modify the template until a build; with `LiveExtraction=true`, it updates live (bounded only by write-if-changed).
- [ ] `ArchPillarLocalizationEmit=false` stops template emission while leaving analyzer diagnostics and runtime lookup functional.
- [ ] Adding a language with the tool (or Poedit, for Portable Object) creates a target file in `OutputPath` with no build or project edit; a subsequent build does not remove or rewrite it.
- [ ] The template is not copied to the application output; target catalogs are (or are embedded), and the runtime finds them.
- [ ] Changing `ArchPillarLocalizationOutputPath` relocates the template and target files together, and the runtime still finds the targets.
- [ ] With `ArchPillarLocalizationEmbedTargets=true` and a single-file publish, the runtime loads target catalogs from embedded resources with no loose files present.
- [ ] A fresh project that references the package and sets a format produces a template on build; running `tool add de` then yields a working German-capable app after the German file is translated — all without touching the project or rebuilding to "enable" German.
