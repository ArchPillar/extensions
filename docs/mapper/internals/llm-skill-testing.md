# ArchPillar.Extensions.Mapper — LLM Skill Testing

How the **Agent Skill** for this library is validated and kept honest over time.

The repository ships an LLM Agent Skill at [`.claude/skills/archpillar-mapper/`](../../../.claude/skills/archpillar-mapper/) — a reference guide that teaches an AI coding assistant to use `Mapper` correctly instead of falling back to AutoMapper-style conventions. This page is for contributors maintaining that skill: why it needs its own tests, what the oracle harness checks, and how to re-run it when the skill or the package changes.

## Why?

A skill is documentation, and documentation about an API has two failure modes the C# compiler never catches:

- **Correctness drift** — the skill describes a method, signature, or behaviour that does not match the shipped package. The library's API is niche enough that a model cannot guess it: an agent given the task *without* the skill invents a plausible-but-nonexistent API (`Configure(IMapperConfiguration)`, `MapEnum`, `CreateMap`, `ProjectTo`) that does not compile. So the skill is load-bearing — and if it drifts from the package, it actively misleads.
- **Transmission failure** — the skill is correct but an agent reading it still produces wrong or non-translatable code, or makes the wrong judgement call (for example reaching for `SymmetricEnumMapper` on a many-to-one enum, which fails the build-time bijection check).

Reading the skill back to ourselves proves neither. The oracle exists to test the skill against the **published package** with a process that is independent of the skill's own text.

## What the oracle checks

The harness lives at [`tools/skill-oracle/mapper/`](../../../tools/skill-oracle/mapper/). It references the **published NuGet packages** (`ArchPillar.Extensions.Mapper`, `ArchPillar.Extensions.Mapper.EntityFrameworkCore`) — what a consumer actually installs, not the in-repo source — and runs skill-generated code through four gates:

1. **Compile** — the code compiles against the package. Catches API/signature drift.
2. **Build-time validation** — each `MapperContext` is constructed and `EagerBuildAll()` runs, exercising coverage validation, enum bijection checks, and circular-reference detection.
3. **Behavior** — each mapper is actually invoked and its output asserted: the many-to-one enum collapse (`High → Urgent`), the dictionary key/value mapping, and the `MapTo` `DeepWithIdentity` merge (collection instance preserved, matched element kept and updated, unmatched removed, new added). Building a mapper is not the same as it mapping correctly — this gate is what proves the latter.
4. **SQL translation** — a representative projection runs against a real relational provider (SQLite) and its values are asserted, proving the expression translates and executes end to end (enum `CASE`, runtime-variable substitution, optional `Include()` join, nested-collection projection).

The runner exits non-zero if any assertion fails (`ORACLE FAILED`). The committed `candidates/` are skill-generated solutions to scenarios chosen to stress judgement, not just recall — a many-to-one enum (must pick `EnumMapper`), a dictionary projection, and an in-place `MapTo` with `DeepWithIdentity`. Each file's header records the scenario that produced it.

## Running it

```bash
cd tools/skill-oracle/mapper && dotnet run
```

A passing run prints `[PASS]` for each context's build and for the SQL-translation check. The project deliberately shadows the repository root `Directory.Build.props`/`Directory.Build.targets` and is **not** registered in `ArchPillar.Extensions.slnx`, so it has no effect on the normal build or CI.

> The companion EF Core package hooks EF Core internals, so the `Microsoft.EntityFrameworkCore.*`
> version pinned in `Mapper.SkillOracle.csproj` must share the EF Core **major** version the
> add-on targets. A mismatch surfaces as a `MissingMethodException` at run time, not at compile
> time.

## Re-test modes

Re-run the oracle whenever **the skill** or **the package** changes:

- **Regression** (package changed) — bump the pinned versions in `Mapper.SkillOracle.csproj`, then `dotnet run`. Recompiling the committed `candidates/` against the new package catches API drift introduced by a release.
- **Skill validation** (skill changed) — regenerate a `candidates/*.cs` file by giving a fresh agent the scenario documented at the top of that file *with the current skill*, paste the output over the candidate, then `dotnet run`. This catches skill drift, and is the only mode that re-exercises the skill text itself.

### Generating candidates in isolation

When an agent generates a candidate, it must work in a directory **outside this repository** —
copy the scenario (and, for the skill arm only, a copy of `SKILL.md`) into a temp folder such as
`/tmp/oracle-run/` and instruct the agent to stay inside it. Running an agent in the repo lets it
read `src/` and `docs/` and reverse-engineer the real API, which contaminates both arms: a baseline
agent is no longer skill-free, and a skill arm no longer measures the skill rather than the source.

> This is best-effort, not a hard sandbox — an agent can still read absolute paths if it
> deliberately tries — so pair the temp folder with an explicit instruction not to access anything
> under the repository.

For a true picture, periodically re-run the **baseline contrast**: give an isolated agent the same
scenario *without* the skill and confirm its output still fails to compile (it hallucinates
`IMapperConfiguration` / `MapEnum` / `ProjectTo`). If the model can do the task without the skill,
the skill has become redundant for that scenario.

## Adding a scenario

1. Write the scenario as a self-contained prompt that names the models and the required outcome.
2. Generate the solution with an agent that has read the skill; save it as `candidates/{Scenario}.cs` with a header comment recording the scenario.
3. Register the new context in `Program.cs` (a `Build(...)` line, and a translation check if it projects).
4. Run `dotnet run` and confirm `[PASS]`.
