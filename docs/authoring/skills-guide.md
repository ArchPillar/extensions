# Skills Authoring Guide

How to write and test an **LLM Agent Skill** for a library in this monorepo. This is the standard
a new skill is expected to follow, and the reference a reviewer (human or agent) checks a skill PR
against. Every library should have one.

The companion guides are [documentation-guide.md](documentation-guide.md) (prose docs) and
[samples-guide.md](samples-guide.md) (example projects).

## Relationship to the general skill-authoring skill

**This guide does not teach how to write a skill in general — that is the job of the
`superpowers:writing-skills` skill.** Invoke and follow it for the craft that is not specific to
this repo:

- frontmatter rules and **Claude Search Optimization** (the `description` is *triggering conditions
  only* — never a workflow summary);
- **progressive disclosure** and token economy (description loads always; `SKILL.md` on trigger;
  `references/` on demand);
- the **RED → GREEN → REFACTOR** testing discipline (watch an agent fail without the skill, write
  the skill, watch it pass, close loopholes);
- naming, flowchart usage, and rationalization-proofing.

Do not restate any of that here or in a skill. **This guide covers only what is specific to
ArchPillar.Extensions**: one skill per library, generating from the spec, the package-vs-supporting
distinction, and the compile/run **oracle** that verifies a skill against the published package.

## What a skill is for

A library skill exists to make an AI assistant use the library **the way it is designed to be
used**, especially where that contradicts the assistant's defaults. Our libraries are deliberately
opinionated, so an unguided model reaches for the wrong-but-familiar pattern:

| Library | The habit the skill must override |
|---------|-----------------------------------|
| Mapper | AutoMapper-style conventions, profiles, attributes, `ProjectTo` |
| Localization | `.resx` / `IStringLocalizer`, name-based lookup, `string.Format` plurals |

The litmus test: **if an agent already produces correct code for the library without the skill, the
skill is redundant for that scenario.** The oracle's baseline arm (below) measures exactly this — a
skill earns its place by turning hallucinated, non-compiling, or wrong-idiom output into correct
output.

A skill encodes the *non-obvious, opinionated* core. It is not a second copy of the docs.

## One skill per library — addons are reference files

- **One skill per library family**, named `archpillar-{library}` (e.g. `archpillar-mapper`,
  `archpillar-localization`), living at `.claude/skills/archpillar-{library}/SKILL.md`.
- **Opt-in add-on packages are `references/` pages, not their own skills.** A developer thinks "I'm
  doing localization", not "I need `Localization.DependencyInjection`". Separate skills would have
  overlapping `description`s and pollute trigger-matching; on-demand reference files keep the
  triggered body small. (This mirrors how [documentation-guide.md](documentation-guide.md) treats an
  add-on as a *feature of the parent*, not a separate library.)
- **Supporting/internal libraries are not addons** — do not present them as packages a consumer
  references. `Mapper` has none worth surfacing; for `Localization`, `.Abstractions`,
  `.MessageFormat`, `.Analyzers`, and `.CodeFixes` are pulled in automatically and must be described
  as such, never as opt-in companions. Only genuinely opt-in packages (`.Tooling`,
  `.DependencyInjection`, `.StringLocalizer`, `.AspNetCore`) go in the package table.

## Generate from the source of truth

Author the skill **from the library's `docs/{library}/internals/SPEC.md`** (plus `features.md` and
`recommendations.md`), not from memory. The SPEC is the design contract and wins on any conflict.
Over-compressing the user-facing summaries loses the conditional nuances that make a skill correct —
generate from the spec, and verify with the oracle.

The skill is the **working subset**: the mental model, the rules an agent gets wrong, and a runnable
example. Exhaustive API surface stays in `docs/` and Context7 (`archpillar/extensions`); link to it,
do not duplicate it.

## SKILL.md skeleton

Use this heading order; adapt the prose, not the structure. (Frontmatter per
`superpowers:writing-skills` — `description` is triggers-only, including the "instead of
`<wrong habit>`" trigger.)

```text
# ArchPillar.Extensions.{Library}

<one paragraph: what it is + the headline property, framed against the opposing approach>

## The mental model (read this first)   — lead with the SMALL surface: the few entry points a
                                           consumer actually writes against
## <Two ways to …> / core usage          — the primary authoring pattern(s)
## Rules that are easy to get wrong       — the numbered "unlearn your habits" list; this is the
                                            highest-value section
## Canonical example                      — one complete, runnable example (real code, no `...`)
## Feature cheat-sheet                    — table: need → API → note
## Production defaults                     — singleton/eager/registration patterns, if any
## <Validate / testing>                    — lightweight; see "What to say about testing" below
## Packages                                — opt-in companions only; supporting libs noted as auto-included
## Deeper guidance                         — pointers to references/ and to docs/ + Context7
```

Keep the body to the working subset; push depth into `references/{topic}.md` loaded on demand. Lead
the mental model with how *small* the everyday surface is — a reader who thinks the library is large
will write more than they need.

### What to say about testing

Tell the consumer to **get the construct to build/validate at least once** (the cheap, high-value
gate — for Mapper that is `EagerBuildAll()` / constructing the `MapperContext`; match the app's
instantiation mode, DI if it uses DI). Output-correctness and integration tests are a secondary "if
you can" — in practice LLM-written code rarely maps/translates *wrong* once it builds, so do not
spend skill tokens over-prescribing correctness tests. (The oracle, by contrast, *does* assert
behavior — that is our regression check, not consumer guidance.)

## House style

Inherit the house style from [documentation-guide.md](documentation-guide.md): LF endings, final
newline, no emoji, no manual table of contents, relative links, `csharp`/`bash`/`text` fences with
real compilable code (no `...`), tables for surfaces and `>` callouts for gotchas.

Any code committed to the oracle harness carries **only the comments a regenerating agent needs**
(the scenario provenance header on a candidate); methodology and how-to-run live in the internals
doc, not in code comments.

## Testing: the skill oracle

A skill is **not done until its output is verified against the published package**, not merely
self-consistent with the spec it was written from. Reading a skill back to yourself proves nothing.
Each library gets an oracle harness under [`tools/skill-oracle/{library}/`](../../tools/skill-oracle/).

**The harness is a standalone project that references the PUBLISHED NuGet packages** — what a
consumer installs, not the in-repo source. It must be isolated from the repo build:

- local `Directory.Build.props` + `Directory.Build.targets` that **shadow** the repo root ones
  (central package management off, repo analyzers off, warnings-not-errors);
- **not** registered in `ArchPillar.Extensions.slnx`, so the normal build and CI ignore it;
- `bin/`, `obj/`, and any generated output (e.g. `Translations/`) gitignored.

### Gates

Run skill-generated code through, in order of value:

1. **Compile** — against the package. Catches API/signature drift. (This is the gate the no-skill
   baseline usually fails.)
2. **Build-time validation** — run whatever the library validates at construction (Mapper's
   `EagerBuildAll`; Localization's generator + `APL` analyzer diagnostics on intentionally bad code).
3. **Behavior** — invoke the construct and assert output where it is cheap and meaningful (the
   many-to-one enum collapse, a `DeepWithIdentity` merge, a rendered default). Secondary to 1–2.
4. **Integration** — exercise the headline runtime behavior end to end (Mapper: a projection
   translates and runs on SQLite; Localization: `dotnet apl` extraction produces the catalog).

The runner prints `[PASS]`/`[FAIL]` per check and exits non-zero on any failure. Gates are
library-specific — pick the ones that prove *this* library's contract.

### Generating candidates in isolation

When an agent generates a candidate, it must work in a directory **outside this repository** (a temp
folder), with the scenario — and, for the skill arm only, a copy of `SKILL.md` — copied in, and an
explicit instruction not to access anything under the repo. Running an agent in the repo lets it read
`src/` or `docs/` and reverse-engineer the API, contaminating both the baseline (no longer skill-free)
and the skill arm (no longer measuring the skill). This is **best-effort, not a hard sandbox** — an
agent can still read absolute paths if it tries — so the instruction matters as much as the folder.

### Re-test modes

- **Regression** (package changed) — bump the pinned versions in the harness `.csproj`, then run.
  Recompiling the committed `candidates/` against the new package catches release drift.
- **Skill validation** (skill changed) — regenerate a candidate from its scenario *with the current
  skill* in an isolated workspace, then run.
- **Baseline contrast** (periodic) — regenerate without the skill and confirm it still fails (or, for
  a library where the wrong approach compiles, produces the wrong idiom). If it succeeds, the skill is
  redundant for that scenario.

### Document the oracle

Each library gets `docs/{library}/internals/llm-skill-testing.md` describing its gates, prerequisites,
and re-test modes, indexed in `docs/{library}/internals/README.md`. Mirror the existing
[Mapper](../mapper/internals/llm-skill-testing.md) and
[Localization](../localization/internals/llm-skill-testing.md) pages.

## Maintaining a skill

A skill is the **working subset, not a changelog of the API** — it should not grow with every
release. When a library gains a feature, decide deliberately whether the skill changes at all.

**Update the skill when the change**:

- alters the **mental model** or the recommended idiom;
- adds a new way to **get it wrong** — a new rule, or a footgun a default-trained agent would hit;
- changes or invalidates an existing rule, example, or cheat-sheet row; or
- introduces a new **opt-in add-on** (a new `references/` page and a package-table row).

**Leave the skill alone when the change** is just another API a competent consumer would use
correctly straight from the docs — a new overload, a niche option, an additive feature with no new
pitfall. That belongs in `docs/` and Context7, which the skill already points to; adding it to the
triggered body only spends always-relevant tokens on rarely-needed detail.

When you do update:

- Prefer a `references/` note or a single cheat-sheet row over expanding the always-triggered body,
  and **prune** whatever the change made stale rather than only adding.
- **Re-run the oracle** (regression, plus skill-validation for the touched area), and add a new
  candidate scenario only if the feature introduces a judgement the existing scenarios don't cover.

The same litmus test governs authoring and maintenance alike: **include something only if its
absence would let an agent get it wrong.**

## Delivery: the skill marketplace

Skills ship through a Claude Code plugin marketplace, treated like NuGet publishing — the monorepo
is the source of truth, the marketplace is a generated mirror:

- **A separate, shared marketplace repo** (`ArchPillar/claude-skills`) is the published artifact and
  the home of the canonical builder: `build_marketplace.py` plus the generated
  `.claude-plugin/marketplace.json` and one plugin per library under `plugins/<lib>/`. The generated
  artifacts are **never hand-edited** — no `plugins/` folder pollutes the libraries repo.
- **Two manifests** make the multi-repo model explicit and auditable:
  - the per-repo **source manifest** (`tools/skill-marketplace/skills.json`) declares the skills
    this repo publishes;
  - the marketplace's **provenance** (`.claude-plugin/sources.json`) records which source repo and
    version each plugin came from, so a repo can update **and delete** its own plugins without
    touching another repo's.
- **Published-gating, derived from CI.** The builder publishes a listed skill only if its package
  appears in this repo's `publish.yml` — an unpublished library gets no plugin, with no hardcoded
  include-list.
- **One shared builder, consumed from the marketplace.** `build_marketplace.py` is canonical in
  `ArchPillar/claude-skills`, so every source repo runs one implementation with no drift. It is
  repo-agnostic: `--source-root` points it at a source-repo checkout (where it reads `.claude/skills/`,
  the manifest, and `publish.yml`), `--into` at the marketplace checkout it merges into. This repo
  keeps only its source manifest under `tools/skill-marketplace/`; the publish and PR-check workflows
  clone the builder rather than vendoring it. `marketplace.json` is regenerated as the union of all
  plugins present, so several repos can publish into one marketplace.
- **A dedicated release workflow** (`.github/workflows/publish-skills.yml`, separate from
  `publish.yml`) clones the marketplace repo, runs its canonical builder against this checkout
  (`--source-root "$GITHUB_WORKSPACE"`), and pushes the regenerated artifacts directly to the
  marketplace repo's `main` on `release: published` (a generated artifact repo, so no PR flow). It
  authenticates as an **org-owned GitHub App** scoped to the marketplace repo (Contents: write),
  minting a short-lived installation token at runtime — no personal PAT. Configure the
  `SKILLS_APP_ID` and `SKILLS_APP_PRIVATE_KEY` secrets on the source repo. The PR-time manifest check
  (`ci.yml`) likewise clones the builder (the marketplace repo is public — a plain shallow clone) and
  runs `--check --source-root "$GITHUB_WORKSPACE"`.

Users install with `/plugin marketplace add ArchPillar/claude-skills` then
`/plugin install archpillar-<library>@archpillar`.

## Required artifacts

Every library skill ships:

| Artifact | Notes |
|----------|-------|
| `.claude/skills/archpillar-{library}/SKILL.md` | The skill, following the skeleton above. |
| `.claude/skills/archpillar-{library}/references/*.md` | On-demand depth and add-on coverage, as earned. |
| `tools/skill-oracle/{library}/` | Isolated harness + committed skill-generated `candidates/`. |
| `docs/{library}/internals/llm-skill-testing.md` | Oracle methodology for this library; indexed in `internals/README.md`. |
| Entry in `tools/skill-marketplace/skills.json` | So the skill publishes to the marketplace (still gated by `publish.yml`). |

## Registering a new skill

1. Author `SKILL.md` (+ `references/`) from the library's SPEC, following the skeleton.
2. Stand up the `tools/skill-oracle/{library}/` harness; run the gates; commit the passing candidates.
3. Run the baseline contrast in isolation and confirm the skill is doing real work.
4. Add `docs/{library}/internals/llm-skill-testing.md` and link it from that `internals/README.md`.
5. Add the skill to `tools/skill-marketplace/skills.json` so it publishes to the marketplace once the
   library is published; the `publish-skills.yml` workflow handles the rest on release.
6. The `tools/` tree is already listed in the top-level [`README.md`](../../README.md); add a sub-entry
   only if the structure note needs it.

## Review checklist

A skill change is ready when every applicable item is true:

- [ ] Followed `superpowers:writing-skills` for the general craft (frontmatter, CSO, progressive
      disclosure, RED-GREEN-REFACTOR) — not duplicated here.
- [ ] Exactly one skill for the library, named `archpillar-{library}`; opt-in add-ons are
      `references/` pages, not separate skills.
- [ ] `description` is triggering-conditions-only and includes the "instead of `<wrong habit>`" trigger.
- [ ] Body follows the skeleton; leads with the small consumer surface; the "rules easy to get wrong"
      section captures the opinionated core.
- [ ] Authored from `internals/SPEC.md`; no duplication of exhaustive API surface (linked instead).
- [ ] Supporting/internal libraries are not presented as opt-in packages.
- [ ] Testing guidance is the lightweight "build/validate at least once"; correctness testing is not
      over-prescribed.
- [ ] An isolated `tools/skill-oracle/{library}/` harness exists, references the **published** package,
      is shadowed out of the repo build, and is not in the solution.
- [ ] The oracle passes (compile + validation + behavior + integration as applicable) and exits
      non-zero on failure; candidates are committed with provenance headers.
- [ ] The baseline contrast was run **in isolation** and confirms the skill changes the outcome.
- [ ] `docs/{library}/internals/llm-skill-testing.md` exists and is indexed.
- [ ] The skill is listed in `tools/skill-marketplace/skills.json`, and its library is published
      (`publish.yml`), so `build_marketplace.py` ships it to the marketplace.
- [ ] For a library change, the skill was updated **only** if it affected the mental model, a rule,
      the idiom, or an add-on — additive API with no new pitfall was left to the docs, and stale
      guidance was pruned.
- [ ] House style matches the documentation guide; LF, final newline, no emoji, real compilable code.
