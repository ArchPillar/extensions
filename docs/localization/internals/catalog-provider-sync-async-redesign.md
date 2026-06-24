# Catalog provider sync/async redesign

> **Implemented.** This redesign has shipped: `ITranslationFormat.Read(Stream)`, `ManifestCatalogProvider.CreateAsync`, and the `LocalizationContext`/`Localizer` `AddProvider` / `LoadCultureAsync` / `PreloadAllAsync` / `CatalogsChanged` surface are in `src/Localization`, with the `…WebAssembly` host helper rewritten over them. This document is retained as the design record.

**Date:** 2026-06-23
**Status:** Design — pending review
**Scope:** `ArchPillar.Extensions.Localization` core (catalog providers, the catalog store, the descriptor and format contracts) and the `…WebAssembly` host helper. New library, no compatibility to preserve.

## Problem

A `Translate` lookup is **synchronous** — it returns a `string` mid-render, in an exception message, anywhere, with no `await`. A synchronous lookup can only resolve what is already in memory, so catalog bytes must be loaded into the snapshot before (or during) the lookup.

Catalog sources fall into two camps:

- **Local** (files on disk, embedded resources): reading is synchronous and effectively instant. They can be loaded *inside* a lookup — this is the on-demand load that makes the library "just work."
- **Network** (the HTTP manifest): fetching is fundamentally asynchronous. There is no way to obtain the bytes synchronously; you can only `await`. Blocking a synchronous lookup to wait for the network **deadlocks** in Blazor WebAssembly (single-threaded) and freezes a thread elsewhere.

The current model papers over this with a uniformly-async `ICatalogProvider` consumed synchronously via `TryGetCompletedResult` / `GetResultBlocking`, plus a bolted-on `CompletesSynchronously` bool. That is sync-over-async by luck: whether a provider loads on the sync path depends on completion *timing*, which is non-deterministic and, for the manifest, fires abandoned HTTP requests from a synchronous lookup.

**Goal:** make synchronous vs asynchronous loading a first-class, type-level distinction in the provider model — no `CompletesSynchronously`, no `TryGetCompletedResult`, no `GetResultBlocking`, no sync-over-async anywhere — while preserving on-demand load.

## Core design

### 1. Split discovery from load

A provider has two distinct responsibilities:

- **Discovery** — enumerate which catalogs exist (`CatalogDescriptor`s: culture, format, identity, and a way to open the bytes). Cheap; reads no catalog bytes.
- **Load** — open and read one catalog's bytes.

Discovery is sealed into **construction**. A provider is *born ready*: once you hold an instance, its descriptor set is known and exposed **synchronously**.

- A **synchronous provider** is constructed synchronously — `new DirectoryCatalogProvider(dir)` scans the directory in its constructor; the resource provider scans loaded assemblies. No async, no init phase.
- An **asynchronous provider** is constructed through a `static async Task<T> CreateAsync(...)` that does its async discovery up front — `ManifestCatalogProvider.CreateAsync(httpClient, manifestUri)` fetches the manifest index and builds the descriptor set. The constructor stays trivial (no I/O); the async work lives in `CreateAsync` because constructors cannot `await`.

This is async *construction*, not the Factory pattern — no factory type, no registry, no indirection. It exists solely because a constructor cannot `await`. The payoff: there is no half-initialized state, no `InitializeAsync` to forget, no nullable index. Discovery's async-ness is sealed inside creation and never leaks onto the lookup path.

### 2. `CatalogDescriptor.Source` is a discriminated union

A descriptor's *source* — how its bytes are obtained — is the place sync vs async lives. It is modelled as a closed discriminated union, faked today as a sealed record hierarchy (migrating to real C# union types when they ship, with the same shape and call sites):

```csharp
public abstract record CatalogSource
{
    private CatalogSource() { }  // closed: exactly these two cases
    public sealed record Synchronous(Func<Stream> Open) : CatalogSource;
    public sealed record Asynchronous(Func<CancellationToken, ValueTask<Stream>> OpenAsync) : CatalogSource;
}
```

```csharp
public sealed class CatalogDescriptor
{
    public required string Culture { get; init; }
    public required string Format  { get; init; }
    public string? Name { get; init; }
    public required CatalogSource Source { get; init; }
    public (string Culture, string Name) Identity => (Culture, Name ?? string.Empty);
}
```

A directory/resource descriptor carries `Synchronous`; a manifest descriptor carries `Asynchronous`. The store decides what to do by pattern-matching the union — not a bool, not a provider type, not a completion-timing guess.

### 3. The parse is synchronous

Both union arms hand the store a `Stream`. Parsing a stream into a `Catalog` is CPU work and is always synchronous:

```csharp
public interface ITranslationFormat
{
    Catalog Read(Stream input);   // replaces Task<Catalog> ReadAsync(Stream, CancellationToken)
    // Write stays as-is.
}
```

The only thing that differs between a sync and an async catalog is *how the stream is obtained*. Everything after the stream — `format.Read(stream)`, dedup, commit — is one shared synchronous code path.

### 4. One update pipeline, three triggers

There is a single pipeline: **load a provider's descriptors for a culture → obtain the stream (per the `CatalogSource` union) → `format.Read` → commit to the snapshot → raise `CatalogsChanged`.** What varies is who pulls the trigger and whether the caller can wait:

| Trigger | Synchronous provider | Asynchronous provider |
|---|---|---|
| Explicit `LoadCultureAsync(culture)` (primary path) | load inline (awaited) | awaited |
| Synchronous lookup miss | load **inline**, resolve immediately — no event needed | enqueue **background** load, return the in-code default now; `CatalogsChanged` fires when it lands |
| Change event (file edited, satellite assembly loaded) | re-discover + load off the lookup path → `CatalogsChanged` | background re-discover/load → `CatalogsChanged` |

"Hot reload" is not a separate mechanism — it is the change-event row of the same pipeline. A directory watcher or the `AssemblyLoad` event pokes a provider to re-discover; the resulting load flows through the identical commit-and-notify path. Same plumbing, different doorbell.

On-demand load is preserved for both kinds:

- Synchronous source: lookup miss loads inline and resolves now — the "just works" behaviour, unchanged.
- Asynchronous source: lookup miss returns the in-code default and loads in the background; the UI re-renders on `CatalogsChanged` (stale-while-revalidate). The explicit `LoadCultureAsync` is the no-flash path when the app wants the translation guaranteed present before render.

### 5. `CatalogsChanged` event

The store raises `CatalogsChanged` after any commit that changed the snapshot. Core owns this signal; the Blazor/components layer subscribes and triggers a re-render. A background load with no notification would be pointless, so emitting the edge is core's responsibility. (Lookups that load synchronously inline do not need the event — the lookup returns the freshly-loaded value directly.)

### 6. Registration: explicit, no builder

- **`context.AddProvider(ICatalogProvider provider)`** is the single entry point. Synchronous providers are `new`'d inline; asynchronous providers are `await …CreateAsync(…)`'d by the caller and then added. The `await` is visible at the call site — deliberately, because async loading is a real cost the reader should see.
- **No builder.** A builder would only relocate the `await` into `BuildAsync` without removing the inherent requirement to be in an async context, while adding a parallel `WithX` surface that drifts from `LocalizerOptions`. `LocalizerOptions` stays a plain settings bag (source culture, missing-argument policy, format precedence, culture allow-list, loading strategy).
- **No `CatalogProviders` on `LocalizerOptions`.** One way to add providers, not two. The ambient store auto-wires its synchronous defaults (resource, directory) internally; a self-contained `LocalizationContext` starts empty and the caller adds providers.
- **Ambient stays synchronous.** The static `Localizer` bootstraps lazily and synchronously, so its auto-wiring brings up only synchronous providers. An asynchronous provider joins *after* bootstrap via an explicit `await …CreateAsync(…)` → `Localizer.AddProvider(…)`. The `…WebAssembly` host helper (`UseArchPillarLocalizationAsync`) performs that await-create-then-add for the common case. There is no async ambient startup.

## What is removed

`ICatalogProvider.CompletesSynchronously`, `TryGetCompletedResult`, `GetResultBlocking`, `LocalizerOptions.CatalogProviders`, and every sync-over-async call site. `ITranslationFormat.ReadAsync` becomes `Read`. The sync/async distinction now lives in the type system (the `CatalogSource` union and the provider's creation), not in a runtime guess.

## Affected surface

- **`ArchPillar.Extensions.Localization`** — `ICatalogProvider` reshaped (discovery returns a synchronous descriptor set; the sync/async load distinction lives in `CatalogSource` — exact interface shape per open question 1); `CatalogDescriptor`/`CatalogSource`; `CatalogStore` (one ingest path — sync inline, async enqueued; the awaited triggers drain the background queue — plus `CatalogsChanged` and registration); `DirectoryCatalogProvider`, `ResourceCatalogProvider`, `ManifestCatalogProvider`; `LocalizationContext`/`Localizer` (`AddProvider`, `LoadCultureAsync`, drop `CatalogProviders`).
- **`ArchPillar.Extensions.Localization.Abstractions`** — `ITranslationFormat.Read`.
- **Format implementations** (XLIFF, ARB, PO) — `ReadAsync` → `Read`.
- **`dotnet apl` tooling** — any use of `ReadAsync` becomes `Read`.
- **`ArchPillar.Extensions.Localization.WebAssembly`** — host helper does `await CreateAsync` → `AddProvider`; subscribes the component layer to `CatalogsChanged`.

## Open questions for planning

These are deliberately unresolved and should be settled while writing the implementation plan:

1. **Provider contract shape.** Two sibling interfaces (`ISynchronousCatalogProvider` / `IAsynchronousCatalogProvider` over a common `ICatalogProvider`) versus one provider that exposes a discovery method returning descriptors (the sync/async distinction living entirely in `CatalogSource`). The descriptor union may make separate provider interfaces redundant.
2. **Background queue mechanics.** Coalescing (do not enqueue the same culture twice), what happens on load failure, whether `CatalogsChanged` carries the culture that changed.
3. **Eager vs on-demand semantics** (`CultureLoading`) in the new model: still meaningful for synchronous providers (load all at startup vs per-culture on miss); for asynchronous providers it is inherently preload-or-background — define whether an explicit "preload all cultures" exists.
4. **Manifest re-discovery.** The manifest has no natural change signal (unlike a file watcher or `AssemblyLoad`); decide whether its descriptor set is fixed for the session or refreshable via an explicit recreate.
5. **Change-event delivery for the resource provider** under the loader-lock constraint (the current `_gate` ordering rules must be preserved: no provider call under the commit lock).

## Anticipated downstream consumers (not in this spec)

Planned follow-on features, listed so the core hooks are designed to serve them — **not** part of this redesign:

- **Async culture-load helpers on the context** — `LoadCultureAsync` is the foundation; already a pillar here.
- **"Ensure-loaded-then-set-culture" integration helpers** — for the ambient localizer, ASP.NET Core request localization (load catalogs around the request-localization step), and a Blazor helper with cookie/localStorage persistence of the chosen culture. These live in the *integration* layers and build on `LoadCultureAsync` + `CatalogsChanged`: core loads catalogs, the integration layer sets `CurrentUICulture`. The two core hooks must stay integration-friendly.
- Server story is **eager-load**, client story is **preload/on-demand** — this sharpens open question 3: treat eager (server) vs preload (client) as a first-class axis when defining the loading-strategy semantics.

## Testing

- The provider contract: a synchronous provider loads inline; an asynchronous provider is never invoked on the synchronous path.
- The update pipeline per trigger (the matrix), including the background-queue path: a lookup miss on an async culture returns the default, then resolves after `CatalogsChanged`.
- No sync-over-async: a test that a synchronous lookup of an unloaded async culture issues no blocking wait and no abandoned request.
- Determinism: the previously-flaky "async culture not loaded until preloaded" scenario passes deterministically (an async source completing synchronously by luck no longer changes behaviour).
- Existing localization suites continue to pass with `ITranslationFormat.Read`.
