# Documentation Authoring Guide

How to write documentation for a library in this monorepo. This is the standard a new
library's docs are expected to follow, and the reference a reviewer (human or agent)
checks a docs PR against. When a rule here conflicts with an older doc, this guide wins
and the older doc should be brought into line.

The companion guide for example projects is [samples-guide.md](samples-guide.md).

## How to use this guide

- **Adding a new library?** Create the [required documents](#required-documents) from the
  canonical skeletons, and register the library in the top-level [`README.md`](../../README.md).
- **Editing existing docs?** Match the skeleton and house style of the doc type you are
  touching. Do not invent a new section order.
- **Reviewing a docs change?** Run the [review checklist](#review-checklist) at the end.

## Where documentation lives

Documentation splits by **audience**. The dividing line is "how do I *use* this?" versus
"how does this *work* and why?" — keep the two apart.

| Location | File(s) | Audience | Purpose |
|----------|---------|----------|---------|
| `src/{Library}/` | `PACKAGE.md` | NuGet consumers | Shipped as the package readme. Self-contained pitch + quick start. |
| `src/{Library}/` | `README.md` | Repo browsers | One-paragraph stub that redirects to `docs/{library}/`. |
| `docs/{library}/` | `README.md`, `getting-started.md`, `features.md` (or `features/`), `recommendations.md`, `CHANGELOG.md`, `KNOWN_ISSUES.md` | Library **users** | How to consume the library — install, first result, every feature, production patterns. |
| `docs/{library}/internals/` | `SPEC.md`, plus architecture and design notes | Library **developers / contributors** | How the library works and why — the design contract, implementation notes, and decisions. |

> **`docs/{library}/` root is user-facing.** Anything aimed at the people *building* the
> library — the spec, architecture write-ups, design-decision records — goes under
> `docs/{library}/internals/`, never at the root. A user browsing the docs should not have to
> step around implementation material.

> Use the library's published name for `src/` folders (`src/Mapper`,
> `src/Pipelines`) and the lowercase short name for the docs folder
> (`docs/mapper`, `docs/pipelines`). This mirrors the existing layout — keep it.

## Required documents

Every library ships the same core set. There is no "skip it because the library is small" —
a small library has a *short* SPEC and a *short* features page, not a missing one. What scales
is depth, not coverage.

**Always required — user docs:**

| Document | Notes |
|----------|-------|
| `src/{Library}/PACKAGE.md` | NuGet readme — self-contained pitch + quick start. |
| `src/{Library}/README.md` | Redirect stub. |
| `docs/{library}/README.md` | Documentation landing page. |
| `docs/{library}/getting-started.md` | Numbered install-to-first-result walkthrough. |
| `docs/{library}/features.md` *or* `docs/{library}/features/` | One entry per feature — see the [scaling rules](#docslibraryfeaturesmd--or-docslibraryfeatures). |

**Always required — developer docs:**

| Document | Notes |
|----------|-------|
| `docs/{library}/internals/SPEC.md` | Design contract — Goals / Non-Goals / concepts. Keep it as short as the surface allows, but it always exists. |

**Conditionally required:**

| Document | Add it when |
|----------|-------------|
| `docs/{library}/recommendations.md` | The library contains any non-obvious logic — a pitfall, an ordering constraint, a "do this, not that" pattern. As soon as correct usage is not self-evident from `getting-started.md`, this doc is required. |
| `docs/{library}/internals/*.md` (architecture, design decisions) | The library has implementation detail or a design rationale worth recording for maintainers — anything a future contributor would otherwise have to reverse-engineer. |
| `docs/{library}/CHANGELOG.md` | The library has a published release. |
| `docs/{library}/KNOWN_ISSUES.md` | Optional — a placeholder tracker, useful once the library ships. |

Scale **depth to surface, never coverage**: a feature with little to say still gets a heading
and a paragraph; it is never dropped. When a file grows long, split it (see `features`) rather
than letting it sprawl.

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

The hub for the full set — the first page a reader lands on.

```text
# ArchPillar.Extensions.{Library}

<one-paragraph description>

## Why?                (the problem, the opposing approach, why this design)
## Quick Start         (the smallest end-to-end example)
## Features            (table: feature → one-line description, mirroring features.md)
## Performance         (BenchmarkDotNet block, only if benchmarks exist)
## Documentation       (relative links to getting-started / features / recommendations; and to internals/SPEC.md for the design contract)
```

A very small surface may present its public types directly (public surface, wire shape,
factories) in place of a `Features` table, the way *Primitives* does — but it still links to
the full doc set under `## Documentation`.

### `docs/{library}/internals/SPEC.md`

The design contract — **developer-facing**, which is why it lives under `internals/` rather
than at the docs root. Opens with Goals / Non-Goals so scope is unambiguous.

```text
# ArchPillar.Extensions.{Library} — Specification

## Overview            (1–2 paragraphs; optional if Goals is self-explanatory)
## Goals
## Non-Goals
## <Conceptual model / Contracts / Core concepts>   (the bulk; one ## per major concept)
## API Surface         (types and signatures, tables for members; for larger surfaces)
## Error philosophy    (how and when the library fails)
## What this library deliberately does not do        (mirror of Non-Goals, concrete)
```

Show full type signatures, not just usage. ASCII diagrams are welcome for execution flow.

### `docs/{library}/internals/` (other developer docs)

`SPEC.md` is the required anchor. Add further developer-facing pages here as the library earns
them — an `architecture.md` walking the implementation, decision records, performance notes —
anything a future contributor would otherwise reverse-engineer from the source. Keep them out
of the user-facing docs root. Once `internals/` holds more than the spec, add an
`internals/README.md` index listing its pages.

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

### `docs/{library}/features.md` — or `docs/{library}/features/`

Document **every** feature. Size each entry to the feature, and split files *before* they get
long — prefer several focused pages over one sprawling file.

- **Minimum** — every feature gets a `##` heading and at least one paragraph: what it does and
  when to reach for it, ideally with a short code example. No feature is omitted because it is
  small.
- **Grow to a full page** — as a feature gains options, behaviour notes, and constraints,
  expand its entry. Order features from most-common to most-advanced.
- **Promote to a folder** — when `features.md` gets long (a feature has earned a full page of
  its own, or the file is pushing past a few hundred lines), convert it to a `features/`
  folder:
  - `features/README.md` — an index: one line per feature, each linking to its page.
  - `features/{feature-name}.md` — one page per feature.
- **Big feature → its own subfolder** — a feature that needs more than one page gets
  `features/{feature-name}/` with its own `README.md` index and sub-pages.

Each page is self-contained: what the feature is, a code example, behaviour notes in `>`
callouts, and constraints. Whichever shape you use (`features.md`, a `features/` folder, or
nested subfolders), link it from the docs `README.md` `## Documentation` section.

### `docs/{library}/recommendations.md` (required once the library has non-obvious logic)

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

- [ ] Every always-required doc exists: `PACKAGE.md`, the src `README.md` stub, the docs
      `README.md`, `getting-started.md`, `features.md` (or `features/`), and
      `internals/SPEC.md`.
- [ ] `recommendations.md` exists if the library has any non-obvious logic, pitfall, or
      ordering constraint.
- [ ] User-facing docs live at `docs/{library}/` root; developer docs (SPEC, architecture,
      decisions) live under `docs/{library}/internals/` — no implementation material at the root.
- [ ] Each doc follows its canonical skeleton — section order matches, nothing reordered.
- [ ] `PACKAGE.md` is self-contained and ends with `## Documentation` and `## License`.
- [ ] `src/{Library}/README.md` is the redirect stub only, not a duplicate of `PACKAGE.md`.
- [ ] The docs `README.md` opens with a one-paragraph description and a `## Why?`.
- [ ] `internals/SPEC.md` states **Goals** and **Non-Goals** up front.
- [ ] `getting-started.md` is numbered and every step has runnable code.
- [ ] Every feature has at least a heading and a paragraph in `features.md` (or `features/`);
      long files are split into a folder rather than left to sprawl.
- [ ] All code fences are language-tagged; examples are real, compilable C# (no `...`).
- [ ] No badges; no emoji (except the top-level README `:)`); no manual table of contents.
- [ ] Inter-doc links are relative and resolve; the package-name link points to its docs folder.
- [ ] The library is registered in the top-level README (Libraries + Repository Structure).
- [ ] LF endings, final newline, no trailing whitespace.
