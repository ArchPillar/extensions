# ArchPillar.Extensions.Localization — internals

Developer- and contributor-facing documentation: how the library works and why. For how to *use*
it, see the user-facing docs at [`../`](../).

| Document | What it is |
|----------|------------|
| [SPEC.md](SPEC.md) | The design contract — Goals, Non-Goals, the conceptual model, the API surface, and the error philosophy. Start here. |
| [DECISIONS.md](DECISIONS.md) | The binding decisions and phase plan for this implementation. **Authoritative over the numbered specs wherever they differ.** |
| [00-architecture.md](00-architecture.md) | System map, glossary, cross-cutting decisions, build order. |
| [01-detection-and-analyzer.md](01-detection-and-analyzer.md) | Attributes, the shared detection core, the `APL` diagnostics. |
| [02-extraction-and-reconciliation.md](02-extraction-and-reconciliation.md) | The extract + reconcile core and the merge algorithm. |
| [03-container-formats.md](03-container-formats.md) | The provider interface and ARB / XLIFF 2.1 / Portable Object. |
| [04-message-format-and-plurals.md](04-message-format-and-plurals.md) | The ICU MessageFormat parser/formatter and embedded CLDR plural data. |
| [05-runtime.md](05-runtime.md) | The runtime call API, fallback chain, lock-free lookup, and hot reload. |
| [06-msbuild-integration.md](06-msbuild-integration.md) | The MSBuild property surface and what the build emits. |
| [TODO.md](TODO.md) | The working implementation tracker. |

The numbered documents (`00`–`06`) are the original detailed design set; `DECISIONS.md` records the
choices actually made for this repository and overrides them on any conflict.

## Why no external dependencies

The runtime, the container-format providers, and the ICU parser use **only the BCL** — no third-party
packages (Decision [D-F](DECISIONS.md); the only data input is the version-pinned CLDR plural table,
embedded as generated source, not a package). Mature libraries exist for most of these pieces
(`Jeffijoe.MessageFormat` for ICU MessageFormat, `Karambolo.PO` for Portable Object, `Xliff.OM` for
XLIFF), so the choice to hand-roll is deliberate. The reasons, graded honestly:

- **One grammar, two consumers (the decisive, architectural reason).** The ICU MessageFormat must be
  parsed at **build time** — inside the netstandard2.0 Roslyn analyzer/generator, to power the `APL`
  diagnostics and the extracted template — *and* formatted at **runtime**, with identical semantics. The
  available libraries are runtime formatters; none runs inside the analyzer or exposes an analyzable AST.
  Adopting one would mean maintaining **two ICU implementations that can drift**, which is strictly worse
  than owning one. This reason is forced, not a preference.
- **Zero-dependency shipping, trimming, and AOT.** A dependency-free package is a frictionless add for a
  consumer (no transitive version conflicts, no supply-chain surface) and is what lets the trim /
  single-file / NativeAOT paths stay clean. `icu.net` is a native ICU wrapper, which would defeat AOT
  outright; a managed dependency would still have to be verified trim/AOT-safe across `net8`–`net10` and
  `netstandard2.0`, and then owned anyway.
- **Control over the data model.** `Catalog`/`CatalogEntry` carry the source fingerprint, translation
  state, previous-source, category, and context that drive reconcile/drift. The libraries' models do not,
  so we would adapt at the boundary regardless — capturing the parsing but re-implementing the semantics.

**The honest caveat.** "Justified to build" is not "justified to ship as proven." The justification is
strong for the ICU parser, the CLDR rules (the *data* is authoritative Unicode CLDR, not reinvented), and
ARB (a JSON convention `System.Text.Json` already serves). It is **weakest for Portable Object and
XLIFF** — format plumbing whose escaping and namespace edge cases are exactly what `Karambolo.PO` and
`Xliff.OM` have already hardened, and where our code is least differentiated. Owning these is defensible
*only* when paired with the correctness evidence maturity would otherwise buy: a CLDR/ICU **conformance
corpus**, a **differential-oracle** test for the formatter, and round-trip/escaping corpora for PO and
XLIFF. Those tests — not the build decision alone — are what close the "not yet battle-tested" gap.

