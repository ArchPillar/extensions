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
