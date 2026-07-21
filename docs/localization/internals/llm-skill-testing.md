# ArchPillar.Extensions.Localization — LLM Skill Testing

How the **Agent Skill** for this library is validated and kept honest over time.

The repository ships an LLM Agent Skill at [`.claude/skills/archpillar-localization/`](../../../.claude/skills/archpillar-localization/) — a reference guide that teaches an AI coding assistant to use this library (in-code ICU defaults, `ILocalizer<T>`, `Localized<T>`, `dotnet apl`) instead of falling back to `.resx` / `IStringLocalizer`. This page is for contributors maintaining that skill. The methodology mirrors [the Mapper skill's](../../mapper/internals/llm-skill-testing.md); see that page for the shared rationale (why a skill needs its own oracle, and the isolation protocol for agent-generated candidates).

## Why a localization oracle is different

Mapper's behaviour is a runtime API, so its oracle compiles and runs code. Localization's headline behaviour is **compile-time**: a Roslyn generator extracts call sites, and an analyzer raises `APL` diagnostics. A meaningful oracle therefore checks four things, not two — and one of them needs the `dotnet apl` global tool and a recent SDK.

## What the oracle checks

The harness lives at [`tools/skill-oracle/localization/`](../../../tools/skill-oracle/localization/). It references the **published NuGet packages** (`ArchPillar.Extensions.Localization`, `…DependencyInjection`) and runs skill-generated code through four gates. (Temporary exception: see "Local-feed mode" under Re-test modes below — while Phase-A currency/number formatting is unpublished, the harness points at a locally packed build instead.)

1. **Compile** — the code compiles against the package (`ILocalizer<T>`, `Localized<T>`, the ambient `Localizer`, DI registration).
2. **Extraction** — a real build runs `dotnet apl extract` and produces `Translations/{Assembly}.en.xliff` containing every key with its in-code ICU default (including `Localized<T>` member-name keys).
3. **Analyzer diagnostics** — intentionally bad code raises the expected `APL` codes (e.g. `APL0001` for a non-constant key, `APL0005` for a `plural` missing its `other` branch).
4. **Runtime resolution** — with no catalogs present, every lookup renders the in-code default, and ICU `plural` pluralizes by culture (CLDR).

## Prerequisites and running

```bash
dotnet tool install --global ArchPillar.Extensions.Localization.Tooling --version <pinned>
cd tools/skill-oracle/localization && dotnet run
```

Without the `dotnet apl` tool the build still succeeds but the auto-extract step warns (`MSB3073`) and gate 2 cannot be checked. Extraction also requires **.NET SDK 9.0.3xx+** — on an older SDK it silently no-ops (see the skill). The project shadows the repo root `Directory.Build.props`/`Directory.Build.targets`, is **not** in `ArchPillar.Extensions.slnx`, and gitignores the generated `Translations/`, so it never affects the normal build or CI.

To check gate 3, add a file with a non-constant key and a plural missing `other`, build, and confirm `APL0001`/`APL0005` appear; then remove it (committing build-breaking code is not the goal).

## Re-test modes

- **Regression** (package changed) — bump the pinned versions in `Localization.SkillOracle.csproj`, then `dotnet run`. Recompiling the committed `candidates/` against the new package catches API drift.
- **Skill validation** (skill changed) — regenerate a `candidates/*.cs` file by giving a fresh agent the scenario at the top of that file *with the current skill*, in an **isolated temp folder** (see the [Mapper page](../../mapper/internals/llm-skill-testing.md#generating-candidates-in-isolation)), then `dotnet run`.
- **Local-feed mode (temporary)** — Phase-A currency/number formatting (`{arg, number, ::currency/USD}`, `ToLocalizedString`) is not yet published to nuget.org. Until it ships, the oracle is pointed at a **locally packed** build of the four consumed packages (`ArchPillar.Extensions.Localization`, `…DependencyInjection`, `…MessageFormat`, `…Abstractions`) at version `1.4.5-phasea-local`, restored from `tools/skill-oracle/localization/local-feed/` (gitignored, not committed) via `tools/skill-oracle/localization/nuget.config` (`local-phasea` source listed before `nuget.org`). Rebuild the feed with:

  ```bash
  dotnet pack src/Localization.Abstractions        -c Release -p:Version=1.4.5-phasea-local -o tools/skill-oracle/localization/local-feed
  dotnet pack src/Localization.MessageFormat       -c Release -p:Version=1.4.5-phasea-local -o tools/skill-oracle/localization/local-feed
  dotnet pack src/Localization                     -c Release -p:Version=1.4.5-phasea-local -o tools/skill-oracle/localization/local-feed
  dotnet pack src/Localization.DependencyInjection -c Release -p:Version=1.4.5-phasea-local -o tools/skill-oracle/localization/local-feed
  ```

  Pass `-p:Version=` (not just `-p:PackageVersion=`) on every pack — `Version` also drives the generated `<dependency>` versions between these project-referenced packages, so all four resolve to the same local version instead of the repo's default `Version` (`0.0.0-local`, see the root `Directory.Build.props`).

  > **TODO (revert once Phase A publishes):** once a Phase-A prerelease of `ArchPillar.Extensions.Localization` ships to nuget.org, switch the oracle back to the published version — bump `Localization.SkillOracle.csproj` off `1.4.5-phasea-local` to the published version, then delete `tools/skill-oracle/localization/nuget.config` and `tools/skill-oracle/localization/local-feed/` so the harness resumes testing the real published package (its whole purpose). Do not let this local-feed detour become silent, permanent drift.

## Baseline contrast

Unlike Mapper, a no-skill baseline here often **compiles** — an agent reaches for the framework's `IStringLocalizer` / `.resx`, which is a real API. So the baseline signal is *qualitative*: without the skill the agent uses the wrong-but-compiling mechanism (resource files, name-based lookup) instead of in-code ICU defaults and `ILocalizer<T>`. The skill's value is redirecting from that habit, not just preventing a compile error.
