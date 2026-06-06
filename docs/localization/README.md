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
