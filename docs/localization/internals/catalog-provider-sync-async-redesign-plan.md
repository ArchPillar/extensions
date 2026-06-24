# Catalog provider sync/async redesign — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make synchronous vs asynchronous catalog loading a first-class, type-level distinction — split discovery from load, born-ready providers, a `CatalogLoad` union on the descriptor, a synchronous parse, and one update pipeline with three triggers — deleting all sync-over-async.

**Architecture:** A provider discovers its catalogs at construction (synchronously for local sources; via `static async CreateAsync` for the HTTP manifest) and is born ready, exposing a synchronous descriptor inventory (`Catalogs`, `CatalogsFor(culture)`). Each `CatalogDescriptor` carries a `CatalogLoad` discriminated union — `Synchronous(Func<Stream>)` or `Asynchronous(Func<…,ValueTask<Stream>>)`. The store pattern-matches the union: synchronous catalogs load inline on a lookup miss; asynchronous catalogs load only via awaited `LoadCultureAsync`/`PreloadAllAsync` or a coalesced background queue, then raise a parameterless `CatalogsChanged`. Parsing is one synchronous `ITranslationFormat.Read(Stream)`.

**Tech Stack:** .NET 8/9/10 (core multi-targets `net8.0;net9.0;net10.0`), C# 14, xUnit. BCL only — no external dependencies in core.

## Global Constraints

- **Zero-warning policy** — warnings are errors in every build; run `dotnet build` and fix all analyzer findings (IDE0007/0008 `var` rules, IDE0032, Roslynator, SonarAnalyzer) before each commit. Do NOT add/modify `.editorconfig` suppressions.
- **No external dependencies in core** — `ArchPillar.Extensions.Localization` uses only BCL types.
- **No `#pragma warning disable`** — fix or configure via `.editorconfig` (with approval); default is fix.
- File-scoped namespaces; `<Nullable>enable</Nullable>`; LF line endings; final newline; no trailing whitespace.
- XML doc comments on all public types/members; none on internal/private.
- Full-word names (no abbreviations). Seal classes not designed for inheritance.
- Tests: xUnit, `MethodName_Scenario_ExpectedBehavior`, Arrange-Act-Assert. Run the full localization suites green: `dotnet test tests/Localization.Tests` (+ `.EndToEnd`, `.AspNetCore`, `.StringLocalizer`, `.DependencyInjection`, `.Tooling`, `.Abstractions`).
- Greenfield: no backward compatibility to preserve — delete obsolete API outright.

## Locked design decisions (from the spec's open questions)

1. **Provider contract** — one `ICatalogProvider`: `IReadOnlyList<CatalogDescriptor> Catalogs { get; }` (enumerable, pre-created at discovery), `IReadOnlyList<CatalogDescriptor> CatalogsFor(CultureInfo culture)` (one culture, may probe satellites), `IDisposable Watch(Action<CatalogDescriptor> onChanged)`. No async methods on the interface — async lives only in `CreateAsync` and `CatalogLoad.Asynchronous`.
2. **Dedup early** — the store skips a `(culture, name)` it already holds before loading; a catalog whose load **fails is marked failed and dropped** (no auto-retry, no timer). Only a `Watch` signal re-attempts.
3. **Eager = load every catalog; OnDemand = load a culture on first need.** One meaning each, applied uniformly; the provider's sync/async nature decides mechanism only. `CultureLoading` **defaults by platform**: `OperatingSystem.IsBrowser() ? OnDemand : Eager`. `PreloadAllAsync()` is the awaited "load everything" for async contexts (server startup).
4. **Manifest is fixed for the session** — `Watch` is a no-op; refresh = recreate via `CreateAsync`.
5. **`Watch` callback carries the changed `CatalogDescriptor`.** The store reloads just that catalog, off the commit lock (provider calls never under `_gate` — preserves the `AssemblyLoad` loader-lock ordering). The store's UI-facing **`CatalogsChanged` is parameterless** (`event Action`).

---

## File structure

**Created:**
- `src/Localization/CatalogLoad.cs` — the `CatalogLoad` discriminated union (closed sealed-record hierarchy).

**Modified:**
- `src/Localization.Abstractions/ITranslationFormat.cs` — `ReadAsync` → `Read`.
- `src/Localization/Formats/{Xliff,Arb,Po}TranslationFormat.cs` — implement `Read`.
- `src/Localization/CatalogDescriptor.cs` — `OpenAsync` → `Load` (the union).
- `src/Localization/ICatalogProvider.cs` — `Catalogs`/`CatalogsFor`/`Watch(Action<CatalogDescriptor>)`; delete `LoadAllAsync`/`LoadAsync`/`CompletesSynchronously`.
- `src/Localization/{Directory,Resource,Manifest}CatalogProvider.cs` — rewrite to the new contract.
- `src/Localization/CatalogStore.cs` — discovery/load split, two paths, background queue, `CatalogsChanged`, `AddProvider`, eager/on-demand, `Watch` wiring.
- `src/Localization/LocalizerOptions.cs` — platform default for `CultureLoading`; delete `CatalogProviders`.
- `src/Localization/LocalizationContext.cs` & `Localizer.cs` — `AddProvider`, `LoadCultureAsync`, `PreloadAllAsync`, `CatalogsChanged`.
- `src/Localization.Tooling/ToolApplication.cs` — `ReadAsync` → `Read` (2 sites).
- `src/Localization.WebAssembly/WebAssemblyHostLocalizationExtensions.cs` — `await CreateAsync` → `AddProvider`; subscribe `CatalogsChanged`.
- Sample `Localization.WasmSample` Program/Home; docs.

**Deleted:**
- `src/Localization/Internal/SynchronousTaskExtensions.cs`.
- `src/Localization/HttpCatalogLoaderExtensions.cs` (the one-shot `AddCatalogsFrom*Async` / old `UseManifestCatalogs` — superseded by provider registration). Move `DefaultManifestPath`/`DefaultManifestFileName` constants onto `ManifestCatalogProvider`.

**Test files to migrate:** `XliffTranslationFormatTests`, `ArbTranslationFormatTests`, `PoTranslationFormatTests`, `ManifestCatalogProviderTests`, `DirectoryCatalogProviderTests`, `ResourceCatalogProviderTests`, `CatalogProviderTests`, `CatalogStoreProviderTests`, `CultureLoadingTests`, `HttpCatalogLoaderTests` (delete/rewrite), `LocalizerTests`, `LocalizerAllocationTests`, `TranslationFormatRegistryTests`, plus `Localization.Tooling.Tests` and `Localization.EndToEnd.Tests`.

---

## Task 1: `CatalogLoad` union and `CatalogDescriptor`

**Files:**
- Create: `src/Localization/CatalogLoad.cs`
- Modify: `src/Localization/CatalogDescriptor.cs`
- Test: `tests/Localization.Tests/CatalogLoadTests.cs`

**Interfaces — Produces:**
- `abstract record CatalogLoad` with `sealed record Synchronous(Func<Stream> OpenCatalog)` and `sealed record Asynchronous(Func<CancellationToken, ValueTask<Stream>> OpenCatalogAsync)`.
- `CatalogDescriptor { string Culture; string Format; string? Name; CatalogLoad Load; (string,string) Identity }`.

- [ ] **Step 1: Write the failing test**
```csharp
using ArchPillar.Extensions.Localization;

namespace ArchPillar.Extensions.Localization.Tests;

public sealed class CatalogLoadTests
{
    [Fact]
    public void Descriptor_CarriesSynchronousLoad_PatternMatches()
    {
        using var stream = new MemoryStream([1, 2, 3]);
        var descriptor = new CatalogDescriptor
        {
            Culture = "de",
            Format = "arb",
            Name = "App.de.arb",
            Load = new CatalogLoad.Synchronous(() => stream)
        };

        Assert.Equal(("de", "App.de.arb"), descriptor.Identity);
        CatalogLoad load = descriptor.Load;
        Stream opened = load switch
        {
            CatalogLoad.Synchronous sync => sync.OpenCatalog(),
            CatalogLoad.Asynchronous => throw new InvalidOperationException("expected synchronous"),
            _ => throw new InvalidOperationException()
        };
        Assert.Same(stream, opened);
    }
}
```

- [ ] **Step 2: Run, verify it fails** — `dotnet test tests/Localization.Tests --filter CatalogLoadTests` → FAIL (compile: `CatalogLoad` not found).

- [ ] **Step 3: Implement `CatalogLoad`**
```csharp
namespace ArchPillar.Extensions.Localization;

/// <summary>
/// How a <see cref="CatalogDescriptor"/>'s bytes are obtained — the place the synchronous/asynchronous
/// distinction lives. A closed two-case discriminated union (faked as a sealed record hierarchy until C# ships
/// union types): a local source is <see cref="Synchronous"/>; a networked source (the HTTP manifest) is
/// <see cref="Asynchronous"/> and is never opened from the synchronous lookup path.
/// </summary>
public abstract record CatalogLoad
{
    private CatalogLoad()
    {
    }

    /// <summary>A catalog whose bytes open synchronously (a file, an embedded resource).</summary>
    /// <param name="OpenCatalog">Opens the catalog's bytes; the caller owns and disposes the stream.</param>
    public sealed record Synchronous(Func<Stream> OpenCatalog) : CatalogLoad;

    /// <summary>A catalog whose bytes are fetched asynchronously (the HTTP manifest).</summary>
    /// <param name="OpenCatalogAsync">Opens the catalog's bytes; the caller owns and disposes the stream.</param>
    public sealed record Asynchronous(Func<CancellationToken, ValueTask<Stream>> OpenCatalogAsync) : CatalogLoad;
}
```

- [ ] **Step 4: Replace `CatalogDescriptor.OpenAsync` with `Load`** — in `src/Localization/CatalogDescriptor.cs`, delete the `OpenAsync` property and add:
```csharp
    /// <summary>How to open the catalog's bytes — synchronous or asynchronous (see <see cref="CatalogLoad"/>).</summary>
    public required CatalogLoad Load { get; init; }
```
Keep `Culture`, `Format`, `Name`, `Identity`. Update the type's XML summary to mention the `Load` union instead of "lazy opener".

- [ ] **Step 5: Run, verify pass** — `dotnet test tests/Localization.Tests --filter CatalogLoadTests` → PASS. (The solution will not fully build yet — providers still reference `OpenAsync`; that is expected and fixed in Tasks 4-6. Build `src/Localization` only after Task 3.)

- [ ] **Step 6: Commit**
```bash
git add src/Localization/CatalogLoad.cs src/Localization/CatalogDescriptor.cs tests/Localization.Tests/CatalogLoadTests.cs
git commit -m "feat(localization): add CatalogLoad union; descriptor carries Load not OpenAsync"
```

---

## Task 2: Synchronous `ITranslationFormat.Read`

**Files:**
- Modify: `src/Localization.Abstractions/ITranslationFormat.cs:25`
- Modify: `src/Localization/Formats/XliffTranslationFormat.cs:37`, `ArbTranslationFormat.cs:48`, `PoTranslationFormat.cs:35`
- Test: the three `*TranslationFormatTests.cs` and `TranslationFormatRegistryTests.cs`

**Interfaces — Produces:** `Catalog ITranslationFormat.Read(Stream input)` (replaces `Task<Catalog> ReadAsync(Stream, CancellationToken)`). `Write` is unchanged.

- [ ] **Step 1: Change the contract** — in `ITranslationFormat.cs` replace line 25:
```csharp
    /// <summary>Reads a <see cref="Catalog"/> from <paramref name="input"/>.</summary>
    /// <param name="input">The stream to read from.</param>
    /// <returns>The parsed catalog.</returns>
    public Catalog Read(Stream input);
```

- [ ] **Step 2: Migrate each format implementation.** The bodies already parse a fully-buffered stream; remove the async machinery. Pattern (apply to all three): rename `public async Task<Catalog> ReadAsync(Stream input, CancellationToken cancellationToken)` → `public Catalog Read(Stream input)`; replace any `await …ReadAsStringAsync(ct)` / `await JsonDocument.ParseAsync(input, …, ct)` / `await reader.ReadToEndAsync(ct)` with the synchronous equivalent (`new StreamReader(input).ReadToEnd()`, `JsonDocument.Parse(input)`); drop `cancellationToken`. Read each file first and keep the parse logic identical — only the I/O calls and signature change.

- [ ] **Step 3: Update the format tests** — in each `*TranslationFormatTests.cs` and `TranslationFormatRegistryTests.cs`, replace `await format.ReadAsync(stream, default)` (or `CancellationToken.None`) with `format.Read(stream)`; make the test methods non-async where that was their only await. Example transformation:
```csharp
// before:  Catalog catalog = await format.ReadAsync(stream, CancellationToken.None);
// after:   Catalog catalog = format.Read(stream);
```

- [ ] **Step 4: Run** — `dotnet build src/Localization.Abstractions src/Localization` (formats compile) then `dotnet test tests/Localization.Tests --filter TranslationFormat` and `dotnet test tests/Localization.Abstractions.Tests`. Expected: PASS. (Full `src/Localization` build still blocked on the store/providers until later tasks — build just the changed projects.)

- [ ] **Step 5: Commit**
```bash
git add src/Localization.Abstractions/ITranslationFormat.cs src/Localization/Formats/ tests/Localization.Tests/*TranslationFormatTests.cs tests/Localization.Abstractions.Tests/
git commit -m "refactor(localization): ITranslationFormat.ReadAsync -> synchronous Read"
```

---

## Task 3: Reshape `ICatalogProvider`

**Files:**
- Modify: `src/Localization/ICatalogProvider.cs`
- Test: covered by provider tasks (4-6).

**Interfaces — Produces:**
```csharp
public interface ICatalogProvider
{
    IReadOnlyList<CatalogDescriptor> Catalogs { get; }
    IReadOnlyList<CatalogDescriptor> CatalogsFor(CultureInfo culture);
    IDisposable Watch(Action<CatalogDescriptor> onChanged);
}
```

- [ ] **Step 1: Rewrite the interface** (delete `LoadAllAsync`, `LoadAsync`, `CompletesSynchronously`):
```csharp
using System.Globalization;

namespace ArchPillar.Extensions.Localization;

/// <summary>
/// A source of translation catalogs. Discovery (finding what catalogs exist) happens at construction — a local
/// provider scans synchronously; the HTTP manifest provider fetches its index in a <c>static CreateAsync</c>. A
/// constructed provider is therefore "born ready": it exposes a synchronous descriptor inventory. Whether a
/// catalog's bytes open synchronously or asynchronously is carried per-descriptor by <see cref="CatalogLoad"/>,
/// so the provider itself has no asynchronous members. This is the public extension point for a custom source.
/// </summary>
public interface ICatalogProvider
{
    /// <summary>Every catalog this provider can enumerate, discovered at construction. Empty when none can be
    /// enumerated up front (a catalog only found by probing a specific culture appears via <see cref="CatalogsFor"/>).</summary>
    public IReadOnlyList<CatalogDescriptor> Catalogs { get; }

    /// <summary>The catalogs this provider has for <paramref name="culture"/> (its exact culture; the store walks
    /// the parent chain). May surface descriptors not in <see cref="Catalogs"/> — a culture satellite is found
    /// only by probing for it. Returns an empty list when it has none.</summary>
    /// <param name="culture">The culture whose catalogs to list.</param>
    public IReadOnlyList<CatalogDescriptor> CatalogsFor(CultureInfo culture);

    /// <summary>Starts watching for change — a file edited, a satellite assembly loaded — invoking
    /// <paramref name="onChanged"/> with the catalog that changed or newly appeared. The store calls this only
    /// when hot reload is enabled. Returns a handle that stops watching when disposed; a provider whose catalogs
    /// never change returns a no-op handle and never invokes the callback.</summary>
    /// <param name="onChanged">Invoked with the descriptor of the changed/new catalog.</param>
    public IDisposable Watch(Action<CatalogDescriptor> onChanged);
}
```

- [ ] **Step 2: Commit** (compiles once providers are migrated; commit with Task 4 if your reviewer prefers a building tree, otherwise alone):
```bash
git add src/Localization/ICatalogProvider.cs
git commit -m "refactor(localization): reshape ICatalogProvider (Catalogs/CatalogsFor/Watch); drop async + CompletesSynchronously"
```

---

## Task 4: `DirectoryCatalogProvider`

**Files:**
- Modify: `src/Localization/DirectoryCatalogProvider.cs`
- Test: `tests/Localization.Tests/DirectoryCatalogProviderTests.cs`

**Interfaces — Consumes:** `ICatalogProvider`, `CatalogLoad.Synchronous`, `CatalogDescriptor`.

- [ ] **Step 1: Write/adapt the failing test** — list and open a catalog synchronously:
```csharp
[Fact]
public void Catalogs_ListsFiles_AndOpensSynchronously()
{
    var dir = Directory.CreateTempSubdirectory();
    try
    {
        File.WriteAllText(Path.Combine(dir.FullName, "App.de.arb"), "{\"greeting\":\"Hallo\"}");
        var provider = new DirectoryCatalogProvider(dir.FullName);

        CatalogDescriptor descriptor = Assert.Single(provider.Catalogs);
        Assert.Equal("de", descriptor.Culture);
        var sync = Assert.IsType<CatalogLoad.Synchronous>(descriptor.Load);
        using Stream stream = sync.OpenCatalog();
        Assert.True(stream.Length > 0);
    }
    finally { dir.Delete(recursive: true); }
}
```

- [ ] **Step 2: Run, verify fail** — `dotnet test tests/Localization.Tests --filter DirectoryCatalogProvider` → FAIL.

- [ ] **Step 3: Rewrite the provider.** Read the current file first; preserve the file-name → culture/format parsing and the directory enumeration. Constructor scans synchronously into a cached `IReadOnlyList<CatalogDescriptor>`; each descriptor's `Load` is `new CatalogLoad.Synchronous(() => File.OpenRead(path))`. `Catalogs` returns the cached list; `CatalogsFor(culture)` filters the cached list to the culture; `Watch` keeps the existing `FileSystemWatcher` but its callback now reports the affected descriptor(s) — on a change event, re-scan and invoke `onChanged(descriptor)` for each file whose `(culture,name)` is new or changed. Key construction:
```csharp
private static CatalogDescriptor Describe(string path) => new()
{
    Culture = CultureFromFileName(path),
    Format  = Path.GetExtension(path),
    Name    = Path.GetFileName(path),
    Load    = new CatalogLoad.Synchronous(() => File.OpenRead(path))
};
```

- [ ] **Step 4: Run, verify pass.** Also migrate the rest of `DirectoryCatalogProviderTests` from `await LoadAsync/LoadAllAsync` to `Catalogs`/`CatalogsFor` + `CatalogLoad.Synchronous.OpenCatalog()`.

- [ ] **Step 5: Commit**
```bash
git add src/Localization/DirectoryCatalogProvider.cs tests/Localization.Tests/DirectoryCatalogProviderTests.cs
git commit -m "refactor(localization): DirectoryCatalogProvider to born-ready Catalogs/CatalogsFor + Synchronous load"
```

---

## Task 5: `ResourceCatalogProvider`

**Files:**
- Modify: `src/Localization/ResourceCatalogProvider.cs`
- Test: `tests/Localization.Tests/ResourceCatalogProviderTests.cs`

**Interfaces — Consumes:** as Task 4. **Produces:** the `AssemblyLoad`-backed `Watch` that emits new satellite descriptors.

- [ ] **Step 1: Adapt tests** — `Catalogs` lists main-assembly embedded catalogs (synchronous open via `GetManifestResourceStream`); `CatalogsFor(culture)` additionally probes the satellite (`assembly.GetSatelliteAssembly(culture)`), returning descriptors whose `Load` is `Synchronous`. Keep an existing embedded-catalog fixture; assert `IsType<CatalogLoad.Synchronous>`.

- [ ] **Step 2: Run, verify fail.**

- [ ] **Step 3: Rewrite.** Read the current file; preserve the embedded-resource enumeration and the satellite probing. Constructor scans the main assembly's manifest resources into the cached `Catalogs` (each `Load = new CatalogLoad.Synchronous(() => assembly.GetManifestResourceStream(name)!)`). `CatalogsFor(culture)` returns the matching cached descriptors **plus** a satellite probe: `GetSatelliteAssembly(culture)` (guarded — returns nothing on `FileNotFoundException`/`CultureNotFoundException`), each found resource a `Synchronous` descriptor. `Watch(onChanged)` subscribes to `AppDomain.CurrentDomain.AssemblyLoad`; on a satellite/embedding assembly load, build the new descriptor(s) and invoke `onChanged(descriptor)` for each — **outside any store lock** (the handler runs while the loader lock is held; it must not call back into a `_gate`-holding path). Preserve the existing debounce/dedup the current provider uses.

- [ ] **Step 4: Run, verify pass.** Migrate the rest of `ResourceCatalogProviderTests`.

- [ ] **Step 5: Commit**
```bash
git add src/Localization/ResourceCatalogProvider.cs tests/Localization.Tests/ResourceCatalogProviderTests.cs
git commit -m "refactor(localization): ResourceCatalogProvider to Catalogs/CatalogsFor + AssemblyLoad Watch emitting descriptors"
```

---

## Task 6: `ManifestCatalogProvider` (async construction)

**Files:**
- Modify: `src/Localization/ManifestCatalogProvider.cs`
- Test: `tests/Localization.Tests/ManifestCatalogProviderTests.cs`

**Interfaces — Produces:** `static Task<ManifestCatalogProvider> CreateAsync(HttpClient httpClient, string manifestUri = DefaultManifestPath, string? sourceCulture = null, CancellationToken cancellationToken = default)`; `const string DefaultManifestPath`, `DefaultManifestFileName` (moved here from `HttpCatalogLoaderExtensions`).

- [ ] **Step 1: Write the failing test** (async creation, born-ready, asynchronous load):
```csharp
[Fact]
public async Task CreateAsync_FetchesIndex_ThenCatalogsListSynchronously_LoadIsAsynchronous()
{
    const string Manifest = "{\"version\":1,\"catalogs\":[{\"culture\":\"de\",\"file\":\"App.de.arb\"}]}";
    HttpClient http = NewClient(new()
    {
        ["/Translations/apl-catalogs.json"] = Ok(Encoding.UTF8.GetBytes(Manifest)),
        ["/Translations/App.de.arb"] = Ok(Encoding.UTF8.GetBytes("{\"greeting\":\"Hallo\"}"))
    });

    var provider = await ManifestCatalogProvider.CreateAsync(http);

    CatalogDescriptor descriptor = Assert.Single(provider.Catalogs);   // born ready, synchronous listing
    Assert.Equal("de", descriptor.Culture);
    var async = Assert.IsType<CatalogLoad.Asynchronous>(descriptor.Load);
    await using Stream stream = await async.OpenCatalogAsync(default);
    Assert.True(stream.Length > 0);
}
```
(Reuse the existing `NewClient`/`Ok` stub handler from `ManifestCatalogProviderTests`; ensure it yields asynchronously — `await Task.Yield()` — like a real `HttpClient`.)

- [ ] **Step 2: Run, verify fail.**

- [ ] **Step 3: Rewrite.** Move the fetch into `CreateAsync`: call the existing `ReadManifestAsync` once, build the descriptor list via `Describe`, pass it to a **private constructor** that stores it. `Describe` now sets `Load = new CatalogLoad.Asynchronous(token => FetchAsync(requestUri, token))`. `Catalogs` returns the stored list; `CatalogsFor(culture)` filters to the culture chain (+ `sourceCulture`); `Watch` returns the no-op handle (manifest is fixed). Keep `ReadManifestAsync`/`ParseManifest`/`FetchAsync`/`CultureFromUri`/`ManifestBase`/`Resolve` as-is.
```csharp
public static async Task<ManifestCatalogProvider> CreateAsync(
    HttpClient httpClient, string manifestUri = DefaultManifestPath,
    string? sourceCulture = null, CancellationToken cancellationToken = default)
{
    if (httpClient is null) throw new ArgumentNullException(nameof(httpClient));
    if (manifestUri is null) throw new ArgumentNullException(nameof(manifestUri));
    var registry = BuiltInTranslationFormats.CreateRegistry();
    IReadOnlyList<string> uris = await ReadManifestAsync(httpClient, manifestUri, cancellationToken).ConfigureAwait(false);
    IReadOnlyList<CatalogDescriptor> catalogs = Describe(httpClient, registry, uris, sourceCulture);
    return new ManifestCatalogProvider(catalogs, sourceCulture);
}
```
(Make `ReadManifestAsync`/`Describe`/`FetchAsync` static, taking `httpClient`/`registry`; the `NoOpWatch` stays.)

- [ ] **Step 4: Run, verify pass.** Migrate `ManifestCatalogProviderTests` (and delete tests asserting the old `LoadAsync`/`CompletesSynchronously`).

- [ ] **Step 5: Commit**
```bash
git add src/Localization/ManifestCatalogProvider.cs tests/Localization.Tests/ManifestCatalogProviderTests.cs
git commit -m "refactor(localization): ManifestCatalogProvider via CreateAsync; descriptors carry Asynchronous load"
```

---

## Task 7: Delete sync-over-async helpers and the one-shot HTTP extensions

**Files:**
- Delete: `src/Localization/Internal/SynchronousTaskExtensions.cs`
- Delete: `src/Localization/HttpCatalogLoaderExtensions.cs`
- Delete/rewrite: `tests/Localization.Tests/HttpCatalogLoaderTests.cs`
- Modify: `src/Localization/Localizer.cs` (remove `AddCatalogsFromHttpAsync`/`AddCatalogsFromManifestAsync`/`UseManifestCatalogs`/`UseCatalogLoader`/`LoadCultureAsync` facade forwards that referenced the deleted API — re-added cleanly in Task 10).

- [ ] **Step 1:** `git rm src/Localization/Internal/SynchronousTaskExtensions.cs src/Localization/HttpCatalogLoaderExtensions.cs tests/Localization.Tests/HttpCatalogLoaderTests.cs`. Ensure `DefaultManifestPath`/`DefaultManifestFileName` already moved to `ManifestCatalogProvider` (Task 6).
- [ ] **Step 2:** Remove the now-dangling facade members from `Localizer.cs` and any `using`/references. Do not build-green yet — the store rewrite (Task 8) follows.
- [ ] **Step 3: Commit**
```bash
git add -A
git commit -m "chore(localization): delete SynchronousTaskExtensions and one-shot HTTP catalog extensions"
```

---

## Task 8: `CatalogStore` rewrite

The core. Read the current `CatalogStore.cs` (740 lines) first. Implement as the sub-tasks below; each is one commit. Preserve the lock discipline: provider calls and parsing run **outside `_gate`**; only the short commit/rebuild holds it.

**Interfaces — Produces (consumed by Tasks 10-11):**
- `void AddProvider(ICatalogProvider provider)`
- `void EnsureCulture(CultureInfo culture)` (synchronous on-demand; inline for sync, enqueue for async)
- `Task LoadCultureAsync(CultureInfo culture, CancellationToken)` and `Task PreloadAllAsync(CancellationToken)`
- `event Action CatalogsChanged`
- internal model: per-provider `(ICatalogProvider Provider, List<Catalog> Catalogs, HashSet<(string,string)> Loaded, HashSet<(string,string)> Failed, IDisposable? Watch)` — replace the parallel arrays with one `ProviderState` list (cleaner for `AddProvider`).

- [ ] **8a — `ProviderState` + `AddProvider` + commit/rebuild.** Replace the parallel `_providers`/`_providerCatalogs`/`_providerLoadedKeys`/`_watches` arrays with a single `List<ProviderState>`. `AddProvider` appends a state, subscribes `Watch` if hot reload is on, and (under the loading rules in 8f) ingests. Keep `Rebuild` layering provider catalogs lowest-first then host catalogs, raising **`CatalogsChanged`** after a snapshot swap that changed content. Test: adding a synchronous provider then resolving sees its catalog.

- [ ] **8b — synchronous ingest with early dedup + fail-marking.** A helper `IngestSynchronous(state, descriptors)`: for each descriptor whose `Identity` is not already in `state.Loaded` or `state.Failed`, pattern-match `Load`; for `CatalogLoad.Synchronous`, `format.Read(sync.OpenCatalog())` inside a `try`; on success add to `Loaded` + the catalog list; on failure add to `Failed` (dropped, no retry). `CatalogLoad.Asynchronous` descriptors are skipped here (handled by 8c/8e). Test: a malformed sync catalog is marked failed and the lookup falls back.

- [ ] **8c — `EnsureCulture` (sync on-demand path).** For each provider, `state.Provider.CatalogsFor(culture)` (+ parent chain); split the returned descriptors by union: `Synchronous` → `IngestSynchronous` inline, then `Rebuild`; `Asynchronous` → hand to the background queue (8d). Mark the culture in-use. Test: lookup of an embedded culture loads inline; lookup of a manifest-only culture returns the default and does not block.

- [ ] **8d — background queue.** A `ConcurrentDictionary<string, Task>` keyed by culture coalesces in-flight async loads (never enqueue a culture already in flight or already loaded). The task opens each `Asynchronous` descriptor (`await OpenCatalogAsync`), `format.Read`, commits under `_gate`, then raises `CatalogsChanged`; on failure marks the descriptor `Failed` and drops it (no retry). Remove the culture from the in-flight map on completion. Test: a lookup miss on an async culture, after the queued task completes, resolves the translation and fired `CatalogsChanged` once.

- [ ] **8e — `LoadCultureAsync` + `PreloadAllAsync`.** `LoadCultureAsync(culture)`: `await` every provider's `Asynchronous` descriptors for the culture (and run `Synchronous` inline), commit, no background queue (caller is awaiting). `PreloadAllAsync()`: the same across **all** known cultures (`Catalogs` ∪ probed). Both raise `CatalogsChanged` once at the end. Test: `await LoadCultureAsync(de)` then a synchronous lookup resolves `de` with no flash.

- [ ] **8f — eager vs on-demand + platform default.** At startup/`AddProvider`: if `CultureLoading.Eager`, ingest **all** catalogs — `Catalogs` for every provider, sync inline, async via the background queue (ambient/sync context) — i.e. "load everything." If `OnDemand`, ingest nothing up front; `EnsureCulture` pulls per culture. The setting's default comes from `LocalizerOptions` (Task 9). Test: eager loads all enumerable cultures at startup; on-demand loads none until requested.

- [ ] **8g — `Watch` wiring.** When hot reload is enabled, `AddProvider` calls `state.Provider.Watch(descriptor => OnCatalogChanged(state, descriptor))`. `OnCatalogChanged` re-ingests just that descriptor (clear its `Identity` from `Loaded`/`Failed`, load per its union — sync inline, async queued), commits, raises `CatalogsChanged`. Runs outside `_gate`. Test: a directory provider stub that fires `onChanged` with an edited descriptor updates the snapshot.

- [ ] **8h** — Run the full `tests/Localization.Tests` and fix fallout. Commit each sub-task (8a…8g) separately with `dotnet build src/Localization` green (zero warnings) before moving on.

---

## Task 9: `LocalizerOptions`

**Files:** Modify `src/Localization/LocalizerOptions.cs`.

- [ ] **Step 1:** Delete the `CatalogProviders` property (lines 61-71) and its doc. Change `CultureLoading` default:
```csharp
    /// <summary>
    /// Whether to load every catalog up front (<see cref="CultureLoading.Eager"/>) or each culture on first use
    /// (<see cref="CultureLoading.OnDemand"/>). Defaults by platform: on-demand in the browser (Blazor
    /// WebAssembly), eager elsewhere. Override to force either.
    /// </summary>
    public CultureLoading CultureLoading { get; init; } =
        OperatingSystem.IsBrowser() ? CultureLoading.OnDemand : CultureLoading.Eager;
```
- [ ] **Step 2:** Build `src/Localization`; fix any reference to the removed `CatalogProviders`. Test: `CultureLoadingTests` still green (add a test asserting the default matches `OperatingSystem.IsBrowser()`).
- [ ] **Step 3: Commit** — `git commit -m "feat(localization): platform-default CultureLoading; remove CatalogProviders option"`.

---

## Task 10: `LocalizationContext` and `Localizer`

**Files:** Modify `src/Localization/LocalizationContext.cs`, `Localizer.cs`.

**Interfaces — Produces:** `LocalizationContext.AddProvider/LoadCultureAsync/PreloadAllAsync/CatalogsChanged`; same on the static `Localizer`.

- [ ] **Step 1:** Add to `LocalizationContext` (forwarding to the store): `void AddProvider(ICatalogProvider)`, `Task<…> LoadCultureAsync(CultureInfo, CancellationToken = default)` (returns `Task`; see 8e), `Task PreloadAllAsync(CancellationToken = default)`, and `event Action CatalogsChanged` (subscribe/unsubscribe to the store's). Ambient auto-wiring stays synchronous (resource + directory only).
- [ ] **Step 2:** Mirror on `Localizer` (static facade → `Ambient`). Remove leftover members from Task 7.
- [ ] **Step 3:** Test (in `LocalizerTests`): `AddProvider` + `LoadCultureAsync` resolves; `CatalogsChanged` fires after a background load. Build green.
- [ ] **Step 4: Commit** — `git commit -m "feat(localization): AddProvider/LoadCultureAsync/PreloadAllAsync/CatalogsChanged on context + facade"`.

---

## Task 11: WebAssembly host helper

**Files:** Modify `src/Localization.WebAssembly/WebAssemblyHostLocalizationExtensions.cs`.

- [ ] **Step 1:** `UseArchPillarLocalizationAsync` now: resolve `HttpClient` from DI, `var provider = await ManifestCatalogProvider.CreateAsync(httpClient, manifestUri, Localizer.Ambient.SourceCultureName, ct); Localizer.AddProvider(provider);` then, per platform default (OnDemand in browser), do **not** eager-load — optionally `await Localizer.LoadCultureAsync(CultureInfo.CurrentUICulture, ct)` so the first render is localized. Return the loaded count or `Task`.
- [ ] **Step 2:** Provide the `CatalogsChanged` → re-render hook for components (a small `ComponentBase` helper or documented `Localizer.CatalogsChanged += StateHasChanged` pattern). Keep it minimal.
- [ ] **Step 3:** Build the package + the `Localization.WasmSample`; update `Program.cs`/`Home.razor` to the new flow (switch = `await Localizer.LoadCultureAsync(culture); CultureInfo.CurrentUICulture = culture;`). Commit.

---

## Task 12: Tooling ripple

**Files:** Modify `src/Localization.Tooling/ToolApplication.cs:448,758`.

- [ ] **Step 1:** Replace `await source.ReadAsync(buffer, CancellationToken.None)` / `await provider.ReadAsync(stream, CancellationToken.None)` with `source.Read(buffer)` / `provider.Read(stream)`; remove the now-redundant `await`/`async` if those were the only suspension points. Build `src/Localization.Tooling`.
- [ ] **Step 2:** Run `tests/Localization.Tooling.Tests` and `tests/Localization.EndToEnd.Tests`; fix fallout. Commit.

---

## Task 13: Full-suite migration and green

- [ ] **Step 1:** `dotnet build` (whole solution) — zero warnings, zero errors. Fix every analyzer finding.
- [ ] **Step 2:** Run every localization suite (the seven listed in Global Constraints). Migrate any remaining test using old API (`OpenAsync`, `LoadAsync`, `CompletesSynchronously`, `CatalogProviders`, `AddCatalogsFrom*`). 
- [ ] **Step 3: Commit** the test migration.

---

## Task 14: Docs and spec sync

- [ ] **Step 1:** Update `docs/localization/recommendations.md`, `docs/localization/README.md`, and the WASM `PACKAGE.md` to the new API (`AddProvider` + `ManifestCatalogProvider.CreateAsync`, `LoadCultureAsync`/`PreloadAllAsync`, `CatalogsChanged`, platform-default loading). Update `docs/localization/internals/*` where the provider model is described, and mark the redesign spec implemented.
- [ ] **Step 2: Commit.**

---

## Self-review

- **Spec coverage:** discovery/load split (Tasks 3-6), born-ready async construction (6), `CatalogLoad` union (1), synchronous `Read` (2), one pipeline/three triggers (8c/8e/8g), `CatalogsChanged` (8a, parameterless), explicit `AddProvider`/no builder/no `CatalogProviders` (8a, 9, 10), ambient stays sync (10), removal of `SynchronousTaskExtensions`/sync-over-async (7, 8), the five open-question resolutions (Design decisions section + 8b dedup/fail, 8f eager, 6 manifest-fixed, 5/8g Watch-carries-descriptor). All covered.
- **Open items folded:** dedup-early + fail-no-retry (8b/8d); `Watch(Action<CatalogDescriptor>)` (3, 5, 8g); platform default (9); `PreloadAllAsync` for the eager-server story (8e).
- **Type consistency:** `Catalogs`/`CatalogsFor`/`Watch(Action<CatalogDescriptor>)`, `CatalogLoad.Synchronous(OpenCatalog)`/`Asynchronous(OpenCatalogAsync)`, `CatalogDescriptor.Load`, `AddProvider`/`LoadCultureAsync`/`PreloadAllAsync`/`CatalogsChanged` used consistently across tasks.
