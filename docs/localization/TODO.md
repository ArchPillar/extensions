# Localization — Working TODO

Small, verifiable steps toward finishing **Phase 10** (D-H namespacing + D-I ambient store).
Rules: **one item at a time**, keep the build and **all tests green at every step**, commit the
relevant TODO update alongside the code. Refine/add items as we learn — this file is iterated just
like the code.

## Done
- [x] 10.1 Contracts: `ILocalizer` / `ILocalizer<T>` / `ILocalizerFactory`, `[TranslationScope]`, `Localized<TSelf>`
- [x] 10.2 Category tier in snapshot/loader; `Localizer<T>`; `LocalizerFactory`; zero-alloc preserved
- [x] 10.3 Ambient store core: `Localization` static, lazy `AssemblyLoad` embedded discovery, `Reset()`
- [x] `ITranslationSource` extension point + built-in `PseudoLocalizationSource`
- [x] ARB / PO / XLIFF persist `Category`; emitted template carries it (dedup by `(category,key)`)
- [x] Detection derives category from the `[TranslationScope]` receiver; `Localized<TSelf>` marked
- [x] Catalog-accepting `Localizer` constructors (test isolation)
- [x] CLI renamed to `dotnet apl`
- [x] TODO app sample (en + de/fr + pseudo smoke test)

## Next (in order)

### A. Correctness cleanups (small, low risk)
- [x] A1. Analyzer: scope `APL0006`/`APL0007` duplicate detection by category
- [x] A2. Typed key registry: namespace constants by category (avoid cross-category key collisions)

### B. ns2.0 multi-target (a `netstandard2.0` library can localize without DI)
- [ ] B1. Add `System.Text.Json` package version to `Directory.Packages.props` (used only on ns2.0)
- [ ] B2. Add `netstandard2.0` to the runtime TFMs; reference `System.Text.Json` conditionally (ns2.0 only)
- [ ] B3. Fix net-only API gaps one by one (`string.Replace(_, _, StringComparison)`, …) via `#if`/polyfills
- [ ] B4. Build green across `netstandard2.0;net8.0;net9.0;net10.0`; all tests still pass

### C. Files-by-default + satellites
- [ ] C1. MSBuild: copy catalogs to output **by default** (per-library naming, collision-free)
- [ ] C2. Ambient store: default beside-binary **directory source** (reuse `CatalogLoader`)
- [ ] C3. Test: dev-mode many-files loading through the ambient store
- [ ] C4. Opt-in embed → **satellite** assemblies (`<name>.<culture>.<ext>`, no `WithCulture` override)
- [ ] C5. Ambient store: lazy per-culture satellite loading via `Assembly.GetSatelliteAssembly` (walk parents)
- [ ] C6. Test: satellite-embedded catalog discovered & loaded

### D. Publish merge
- [ ] D1. `dotnet apl merge`: gather → resolve precedence → flatten → one catalogue per culture
- [ ] D2. Publish MSBuild target invoking the tool
- [ ] D3. Test: merged catalogue resolves identically to the dev many-files path

### E. DI integration (10.5) feeds the ambient
- [ ] E1. `AddArchPillarLocalization` populates the ambient store (calling-assembly default namespace)
- [ ] E2. `IStringLocalizer` adapter reads the ambient store
- [ ] E3. Test

### F. Samples (after the milestones)
- [ ] F1. No-DI validation-style library sample (batteries included, files)
- [ ] F2. Exception-text sample (localize from a static / exception context)
- [ ] F3. Revisit existing samples for the ambient/files model

### G. Docs
- [ ] G1. Update `integration.md` / `runtime.md` for ambient + files + satellites + publish merge

### H. Validation
- [ ] H1. Spike: embedded/satellite discovery under trimming / single-file / AOT
