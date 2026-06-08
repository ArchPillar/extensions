# Documentation Authoring Guide

How to write documentation for a library in this monorepo. This is the standard a new
library's docs are expected to follow, and the reference a reviewer (human or agent)
checks a docs PR against. When a rule here conflicts with an older doc, this guide wins
and the older doc should be brought into line.

The companion guide for example projects is [samples-guide.md](samples-guide.md).

## How to use this guide

- **Adding a new library?** Pick a tier (below), create the required docs from the
  canonical skeletons, and register the library in the top-level [`README.md`](../../README.md).
- **Editing existing docs?** Match the skeleton and house style of the doc type you are
  touching. Do not invent a new section order.
- **Reviewing a docs change?** Run the [review checklist](#review-checklist) at the end.

## Where documentation lives

Each library has two homes for prose, with no overlap in responsibility:

| Location | File(s) | Audience | Purpose |
|----------|---------|----------|---------|
| `src/{Library}/` | `PACKAGE.md` | NuGet consumers | Shipped as the package readme. Self-contained pitch + quick start. |
| `src/{Library}/` | `README.md` | Repo browsers | One-paragraph stub that redirects to `docs/{library}/`. |
| `docs/{library}/` | `README.md`, `SPEC.md`, `getting-started.md`, `features.md`, `recommendations.md`, `CHANGELOG.md`, `KNOWN_ISSUES.md` | Repo readers, contributors | The full documentation set. |

> Use the library's published name for `src/` folders (`src/Mapper`,
> `src/Pipelines`) and the lowercase short name for the docs folder
> (`docs/mapper`, `docs/pipelines`). This mirrors the existing layout — keep it.

## The tiered model

Not every library needs every document. Pick a tier by **public API surface**, not by how
much you have to say. A small library with a big README is a smell; split it only when the
surface genuinely warrants a SPEC.

| Document | Tier 1 — Small (e.g. Primitives) | Tier 2 — Medium (e.g. Pipelines, Commands) | Tier 3 — Large (e.g. Mapper) |
|----------|:--:|:--:|:--:|
| `PACKAGE.md` (src) | required | required | required |
| `README.md` (src stub) | required | required | required |
| `docs/.../README.md` | required | required | required |
| `SPEC.md` | optional | required | required |
| `getting-started.md` | optional | required | required |
| `features.md` | — | optional | required |
| `recommendations.md` | — | optional | required |
| `CHANGELOG.md` | when released | when released | when released |
| `KNOWN_ISSUES.md` | optional | optional | optional |

Rules of thumb for sizing:

- **Tier 1 (Small):** a handful of types, no real configuration story. The `docs/.../README.md`
  carries everything — public surface, wire shape, a quick start. No SPEC until the design
  needs defending. *Primitives* is the model here.
- **Tier 2 (Medium):** a focused feature with a DI story and a few moving parts. Add a SPEC
  (Goals / Non-Goals / contracts) and a numbered `getting-started.md`. Add `recommendations.md`
  once there are real "do this, not that" patterns worth writing down.
- **Tier 3 (Large):** a broad surface with many features. Split feature documentation out of
  getting-started into its own `features.md`, and keep `recommendations.md` current. *Mapper*
  is the model here.

Promote a library to a higher tier when its surface grows — don't pre-build docs for features
that don't exist yet.

## House style

These apply to every document.

- **Voice:** professional but plain, second person ("you register…"). Explain *why* a design
  choice was made, not just what it does — the `## Why?` opening is a load-bearing convention.
- **No badges, no emoji.** The single exception is the established friendly `:)` in the
  top-level [`README.md`](../../README.md) intro. Do not add build/version/coverage badges.
- **No table of contents.** Rely on linear flow and the host's generated heading nav.
- **Headings:** one `#` H1 = the library or document title. Sections are `##`, sub-topics
  `###`. Title an H1 for a library as its full package name, e.g. `# ArchPillar.Extensions.Mapper`.
- **Code fences are always `csharp`** unless the content is a shell command (`bash`), project
  file (`xml`), JSON payload (`json`), or raw output (`text`). Every example must compile in
  principle — no pseudo-code, no `...` standing in for required syntax.
- **Tables** for feature lists, API surfaces, and option matrices. Prose for rationale.
- **Callouts** use a leading `>` blockquote for notes, gotchas, and migration warnings.
- **Links** between docs are relative (`getting-started.md`, `../../README.md`). Link the
  package name to its docs folder when first mentioned across libraries.
- **Line endings LF, UTF-8, final newline, no trailing whitespace** — same as code.
- **Spelling:** match the existing en-GB/en-US mix already in a file; don't churn a doc just
  to reskin spelling.

## Canonical skeletons

Use these heading orders verbatim. Omit a section only if it genuinely does not apply, and
never reorder. Adapt the prose, not the structure.

### `src/{Library}/PACKAGE.md` (NuGet readme)

Self-contained — a NuGet visitor sees only this. One-line description under the title, then:

```text
# ArchPillar.Extensions.{Library}

<one-paragraph description: what it is, the headline property>

## Why?
## Quick Start         (show the primary path; for DI libraries show DI first, then direct)
## <1–3 surface sections>   (e.g. "DI extension surface", "Wire shape", "Telemetry")
## Documentation       (link to the GitHub repo / docs folder)
## License
```

Keep it to roughly 60–110 lines. It is a pitch plus a runnable quick start, not a manual.

### `src/{Library}/README.md` (redirect stub)

Exactly this shape — do not duplicate `PACKAGE.md` here:

```text
# ArchPillar.Extensions.{Library}

This is the source code for the `ArchPillar.Extensions.{Library}` library.

For documentation, see [docs/{library}/](../../docs/{library}/).
```

### `docs/{library}/README.md` (documentation landing page)

The hub for the full set. For Tier 1 this is the whole manual.

```text
# ArchPillar.Extensions.{Library}

<one-paragraph description>

## Why?                (the problem, the opposing approach, why this design)
## Quick Start         (the smallest end-to-end example)
## Features            (table: feature → one-line description)   [Tier 2/3]
## Performance         (BenchmarkDotNet block, only if benchmarks exist)
## Documentation       (relative links to getting-started / features / recommendations / SPEC)
```

For Tier 1, replace `Features`/`Documentation` with the type-by-type surface the way
*Primitives* does (public surface, wire shape, factories, etc.).

### `docs/{library}/SPEC.md`

The design contract. Opens with Goals / Non-Goals so scope is unambiguous.

```text
# ArchPillar.Extensions.{Library} — Specification

## Overview            (1–2 paragraphs; optional if Goals is self-explanatory)
## Goals
## Non-Goals
## <Conceptual model / Contracts / Core concepts>   (the bulk; one ## per major concept)
## API Surface         (types and signatures, tables for members)   [Tier 3]
## Error philosophy    (how and when the library fails)
## What this library deliberately does not do        (mirror of Non-Goals, concrete)
```

Show full type signatures, not just usage. ASCII diagrams are welcome for execution flow.

### `docs/{library}/getting-started.md`

A linear, numbered walkthrough from install to first working result. Number the steps
(`## 1. Install`, `## 2. …`) — this is the established pattern in Commands and Pipelines and
the easiest for a reader to follow.

```text
# Getting started with ArchPillar.Extensions.{Library}

## 1. Install
## 2. <define the first thing>
## 3. <wire it / write a handler>
## 4. <run it>
## N. <optional: DI, telemetry, validation — clearly marked optional>
```

Every step carries a complete, copy-pasteable code block. End with a pointer to `features.md`
or `recommendations.md` for what to read next.

### `docs/{library}/features.md` (Tier 3)

One `##` per feature, each a self-contained deep dive: what it is, a code example, behaviour
notes (in `>` callouts), and constraints. Order from most-common to most-advanced.

### `docs/{library}/recommendations.md` (Tier 2 optional / Tier 3)

Production patterns. One `##` per recommendation, phrased as an imperative
(`## Register as Singleton`, `## Validate handler registrations at startup`), each with a short
rationale and a code example. Use `>` callouts for anti-patterns. Link to a sample where one
demonstrates the pattern.

### `docs/{library}/CHANGELOG.md`

Start it when the library has a published release. Until then, a single `## Unreleased` with
`### Behaviour changes` / `### Telemetry` subsections. Breaking changes carry an inline
`*Migration:*` note.

### `docs/{library}/KNOWN_ISSUES.md`

Optional. When present and empty, state it plainly:

```text
# Known Issues

This file tracks known issues, design concerns, and potential improvements in the
ArchPillar.Extensions.{Library} library.

**Status: No known issues at this time.**
```

## Registering a new library

When you add a library, also:

1. Add a `### [ArchPillar.Extensions.{Library}](docs/{library}/)` entry to the **Libraries**
   section of the top-level [`README.md`](../../README.md), with the same one-paragraph style
   as the existing entries.
2. Add its `src/`, `tests/`, `benchmarks/`, `docs/`, and `samples/` rows to the **Repository
   Structure** tree in that README.
3. Cross-link: if the library builds on another (as Commands builds on Pipelines and
   Primitives), say so in the `## Why?` of both the `PACKAGE.md` and the docs `README.md`.

## Review checklist

A docs change is ready when every applicable item is true:

- [ ] The library's tier is correct, and every **required** doc for that tier exists.
- [ ] No doc was added that the tier doesn't call for (no padding).
- [ ] Each doc follows its canonical skeleton — section order matches, nothing reordered.
- [ ] `PACKAGE.md` is self-contained and ends with `## Documentation` and `## License`.
- [ ] `src/{Library}/README.md` is the redirect stub only, not a duplicate of `PACKAGE.md`.
- [ ] The docs `README.md` opens with a one-paragraph description and a `## Why?`.
- [ ] A SPEC (if present) states **Goals** and **Non-Goals** up front.
- [ ] `getting-started.md` (if present) is numbered and every step has runnable code.
- [ ] All code fences are language-tagged; examples are real, compilable C# (no `...`).
- [ ] No badges; no emoji (except the top-level README `:)`); no manual table of contents.
- [ ] Inter-doc links are relative and resolve; the package-name link points to its docs folder.
- [ ] The library is registered in the top-level README (Libraries + Repository Structure).
- [ ] LF endings, final newline, no trailing whitespace.
