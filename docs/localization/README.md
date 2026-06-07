# ArchPillar.Extensions.Localization — Specification Set

A user-interface translation library for .NET: extract translatable strings from
call sites with a Roslyn source generator, hand the resulting files to translators
in standard formats (ARB, XLIFF 2.1, Portable Object), and load translations at
runtime as pluggable overrides.

> **`DECISIONS.md` is authoritative where it differs from the numbered specs.** The
> numbered documents (`00`–`06`) are the original design set; `DECISIONS.md` records
> the choices made for this repository and overrides the specs on any conflict.

## Read in this order

1. **`DECISIONS.md`** — the binding decisions for this implementation, the phase plan,
   and every adjustment to the original specs. **Start here.**
2. **`00-architecture.md`** — system map, glossary, cross-cutting decisions, build order.
3. **`04-message-format-and-plurals.md`** — `MessageFormat` (ICU MessageFormat parser/
   formatter + embedded CLDR plural data). Foundational; built first.
4. **`01-detection-and-analyzer.md`** — attributes, the shared detection core, diagnostics.
5. **`02-extraction-and-reconciliation.md`** — the extract + reconcile core, the merge algorithm.
6. **`03-container-formats.md`** — the provider interface + ARB, XLIFF 2.1, Portable Object.
7. **`05-runtime.md`** — call API, fallback chain, lock-free lookup, optional hot reload.
8. **`06-msbuild-integration.md`** — the MSBuild property surface and what the build emits.

## Using the library (practical guides)

The numbered documents above are the design set. For how to *use* the shipped library, read:

- **`runtime.md`** — the ambient store, the `ILocalizer` / `ILocalizer<T>` API and categories, loading from
  files vs. embedded/satellites, publishing (merge per culture), and the trimming / single-file / AOT matrix.
- **`integration.md`** — dependency injection, the composing `IStringLocalizer` adapter, **migrating from
  `IStringLocalizer` / `.resx`** (the `L(...)` marker, on-by-default indexer extraction, what is *not*
  extracted), Blazor WebAssembly, and the sample index.
- **`message-format.md`** — the ICU MessageFormat grammar the defaults and translations are written in.
