# Samples Authoring Guide

How to write an example project for a library in this monorepo. This is the standard a new
sample is expected to follow, and the reference a reviewer (human or agent) checks a sample
PR against.

The companion guide for prose documentation is
[documentation-guide.md](documentation-guide.md).

## What a sample is for

A sample exists to **show one library working in a realistic but minimal setting**. It is not
a starter template and not architectural guidance — the top-level
[`README.md`](../../README.md) says exactly this:

> The sample projects exist solely to demonstrate these libraries in action. They are not
> intended as guidance on how to structure services or products.

Keep that framing. A sample teaches the library's API and idioms; it does not teach how to
build a product.

## Naming

Sample project and folder names follow one scheme:

```text
{Library}.{Scenario}Sample
```

- `{Library}` is the library's short name: `Mapper`, `Pipelines`, `Commands`, `Primitives`.
- `{Scenario}` describes the setting: `WebShop`, `Builder`, `Host`, `WebApi`, `Notes`.
- The `Sample` suffix is always present.

Samples live under `samples/{Library}/{Library}.{Scenario}Sample/`.

> **Existing samples predate this scheme and should be renamed as a follow-up** so the whole
> tree is consistent:
>
> | Current | Canonical |
> |---------|-----------|
> | `samples/Mapper/WebShop` | `samples/Mapper/Mapper.WebShopSample` |
> | `samples/Mapper/WebShop.OData` | `samples/Mapper/Mapper.WebShopODataSample` |
> | `samples/Pipelines/Pipeline.BuilderSample` | `samples/Pipelines/Pipelines.BuilderSample` |
> | `samples/Pipelines/Pipeline.HostSample` | `samples/Pipelines/Pipelines.HostSample` |
> | `samples/Commands/Command.WebApiSample` | `samples/Commands/Commands.WebApiSample` |
> | `samples/Commands/Command.HostSample` | `samples/Commands/Commands.HostSample` |
>
> New samples use the canonical scheme from day one.

## Choosing the scenario and project type

Match the smallest project type that shows the library honestly. Three tiers, by how much
context the feature needs:

| Tier | Project type | When | Reference |
|------|--------------|------|-----------|
| **Minimal** | Single-file console (`Microsoft.NET.Sdk`, `Exe`) | The feature is self-contained; no DI or hosting needed. | `Pipelines.BuilderSample` (~60 lines) |
| **Host** | Console on `Microsoft.Extensions.Hosting` | The DI / hosting story is part of the point. | `Commands.HostSample`, `Pipelines.HostSample` |
| **Web** | ASP.NET Core Minimal API or Controllers (`Microsoft.NET.Sdk.Web`) | The library's natural home is a web app (request mapping, command dispatch over HTTP). | `Mapper.WebShopSample`, `Commands.WebApiSample` |

Prefer the lowest tier that still tells the truth. A web app to demonstrate a pure middleware
builder is overkill; a console `Console.WriteLine` to demonstrate EF Core projection is
under-kill.

If a library needs more than one scenario (e.g. a no-DI path *and* a hosted path, or a REST
path *and* an OData path), ship one focused sample per scenario rather than one sample with
flags.

## Project structure

Pick the layout that fits the scenario; don't mix both.

- **Domain-folder layout** — group by business entity. Best for Host/Minimal samples built
  around one or two entities. Mirrors `Commands.HostSample`:

  ```text
  Commands.HostSample/
    Orders/
      Commands/        # CreateOrder.cs, CancelOrder.cs, *Handler.cs
      InMemoryOrderStore.cs
    Program.cs
  ```

- **Layer-folder layout** — group by responsibility. Best for Web samples with several
  entities. Mirrors `Mapper.WebShopSample`:

  ```text
  Mapper.WebShopSample/
    Models/            # domain entities
    Projections/       # read-side DTOs
    Parameters/        # request DTOs
    Mappers/           # the MapperContext
    Endpoints/         # route handlers (one static class per resource)
    Data/              # DbContext + Seeder
    Program.cs
  ```

Within either layout, **one type per file**, named after the type. Keep DTO suffixes
consistent within a sample (`...Request` or `...Parameters`, not both).

## The file-header comment block

Every sample's `Program.cs` opens with a banner comment that states what the sample
demonstrates, as a bulleted list, ending with a note on where the rest of the code lives. This
is the established convention — follow the shape of `Commands.HostSample`:

```csharp
// ---------------------------------------------------------------------------
// {Library}.{Scenario}Sample
//
// Demonstrates ArchPillar.Extensions.{Library} in a <setting>:
//   - <feature one — the headline thing>
//   - <feature two>
//   - <edge case or failure path worth showing>
//
// Domain types live under <Folder>/ — one file per class.
// ---------------------------------------------------------------------------
```

Beyond the banner, comment **decisions, not mechanics**: explain the read/write split, why a
DTO is separate from a command, why a connection is held as a singleton — not what a `for`
loop does. Match the density of the surrounding samples: enough that a reader learns the
*why*, never a running narration.

## The sample README

**Every sample has a `README.md`** at its root. (None do today — add one when you touch a
sample, and always for a new one.) Keep it short:

```text
# {Library}.{Scenario}Sample

<one sentence: what this sample demonstrates>

## What it shows
- <bullet per feature, mirroring the Program.cs banner>

## Running
```bash
dotnet run --project samples/{Library}/{Library}.{Scenario}Sample
```
<one line on what to expect: console output, or the URL + a route to hit>

## Notes
<optional: data is in-memory and reseeded each run; auth token to use; etc.>
```

The README and the `Program.cs` banner should agree on the feature list — they are the same
list in two places, on purpose.

## Data and reproducibility

- **A sample must run with a single `dotnet run`** and no external setup. Use an in-memory
  store, in-memory/SQLite EF Core, or a seeded local file — never a connection to a service
  the reader doesn't have.
- **Seed deterministic data** so output is stable run to run. Put seeding in a `Seeder` (Web)
  or an `InMemory...Store` (Host/Minimal).
- If a sample *can* use a richer backend optionally (e.g. PostgreSQL when present, SQLite
  otherwise), it must still default to the zero-setup path.
- **No external package dependencies** beyond what the demonstrated library and the host
  framework require. A console-output helper is fine if a sample already uses one; don't add
  new ones casually.

## Showing the value

- Where the library replaces hand-written code, **show the contrast** — a brief "without this"
  snippet in a comment or the README makes the payoff concrete.
- Exercise the **failure paths**, not just the happy path: a validation failure, a
  short-circuit, a not-found. The existing Command and Pipeline samples all do this and it is
  the most instructive part.
- Keep scope honest to the tier: Minimal stays ~60–150 lines; Host ~200–500; Web as large as
  the scenario needs, but every file must earn its place.

## Registering a new sample

1. Add the sample to the **Repository Structure** tree in the top-level
   [`README.md`](../../README.md), with a trailing comment describing it.
2. Add the project to the solution (`ArchPillar.Extensions.slnx`).
3. Confirm it builds clean under the repo's **zero-warnings** policy — samples are held to the
   same analyzer bar as `src/` (see [`CLAUDE.md`](../../CLAUDE.md)).

## Review checklist

A sample change is ready when every applicable item is true:

- [ ] Folder and project are named `{Library}.{Scenario}Sample`.
- [ ] Project type is the lowest tier that demonstrates the feature honestly.
- [ ] Structure uses one layout (domain-folder *or* layer-folder), one type per file.
- [ ] `Program.cs` opens with the banner comment listing what's demonstrated.
- [ ] Comments explain decisions, not mechanics; density matches existing samples.
- [ ] A root `README.md` exists with What it shows / Running / Notes, agreeing with the banner.
- [ ] Runs with a single `dotnet run`, no external setup, deterministic seeded data.
- [ ] No new external package dependencies beyond the library and host framework.
- [ ] At least one failure/edge path is exercised, not just the happy path.
- [ ] Builds with zero warnings under the repo analyzers.
- [ ] Registered in the top-level README Repository Structure and the solution file.
