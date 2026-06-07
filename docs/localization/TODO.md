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

### B. ~~ns2.0 multi-target~~ — DROPPED
Maintainer supports nothing before .NET 8 (no .NET Framework, no `netstandard` for consumers). The runtime
stays `net8.0;net9.0;net10.0`; a localizing library is itself net8+. `System.Text.Json`/`System.Xml` are
in-box, so no package deps and no `#if`/polyfill work. (B1's package-version prep was reverted.)

### C. Files-by-default + satellites
- [x] C1. MSBuild: copy catalogs to output **by default** (per-library naming — `Translations/<AssemblyName>.<culture>.<ext>`, collision-free; verified end-to-end)
- [x] C2. Ambient store: default beside-binary **directory source** (layered embedded < directory < host; configurable `TranslationsDirectory`)
- [x] C3. Test: dev-mode files loading through the ambient store (`TranslationsDirectory_LoadsCatalogsFromFilesBesideTheBinary`)
- [ ] C4. Opt-in embed → **satellite** assemblies (`<name>.<culture>.<ext>`, no `WithCulture` override)
- [x] C5. Ambient store: lazy per-culture satellite loading via `Assembly.GetSatelliteAssembly` (walk parents); `[LocalizationSatelliteCatalogs]` marker; zero-cost fast path for files-only apps
- [x] C6. Test: satellite-embedded catalog discovered & loaded (`SatelliteCatalog_IsLoadedLazilyForTheRequestedCulture`)

### D. Publish merge
- [ ] D1. `dotnet apl merge`: gather → resolve precedence → flatten → one catalogue per culture
- [ ] D2. Publish MSBuild target invoking the tool
- [ ] D3. Test: merged catalogue resolves identically to the dev many-files path

### E. DI integration (10.5) feeds the ambient
- [x] E1. `AddArchPillarLocalization` populates the ambient store + registers `ILocalizer`/`ILocalizer<T>` over it (kept `Localizer` for direct injection; dropped the `Localizer`-instance overload)
- [x] E2. `IStringLocalizer`/`<T>`/factory adapters read the ambient (no found-awareness — just value-or-name)
- [x] E3. Tests restructured: functionality uses explicit catalogs via `Localization.AddCatalog` + `Reset()`; WASM sample fed via the ambient

### F. Samples (after the milestones)
- [ ] F1. No-DI validation-style library sample (batteries included, files)
- [ ] F2. Exception-text sample (localize from a static / exception context)
- [ ] F3. Revisit existing samples for the ambient/files model

### G. Docs
- [ ] G1. Update `integration.md` / `runtime.md` for ambient + files + satellites + publish merge
- [ ] G2. Publishing guidance: **files are trim/AOT-safe (the default)** — loose on disk, parsed with DOM
  APIs, no reflection; for **mobile/WASM/AOT/trimmed apps prefer files** (ideally one merged bundle per
  culture). Embed is opt-in and needs the trimmer rooting from H1. Note the globalization-data caveat
  (an app on `InvariantGlobalization` can't select non-default cultures — standard .NET advice).

### H. Validation
- [ ] H1. **(Embed path only — the default files path is already trim/AOT-safe)** Validate the *embedded*/
  satellite discovery under trimming / single-file / AOT: root the `[LocalizationCatalog]` attribute,
  embedded resources, and satellites against the trimmer. The files path needs no spike. Sanity-check the
  `JsonDocument`/`XDocument` DOM parsers under AOT while here.
