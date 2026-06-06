# ArchPillar.Extensions.Localization.Tooling

The `dotnet` tool (`archpillar-loc`) for the `ArchPillar.Extensions.Localization` family. It works on
demand, never as part of the build:

- `extract` — read the source-language template the generator baked into a built assembly and write it
  to the translations directory.
- `add <lang>` — create a new target catalog from the template (every entry untranslated).
- `sync` — reconcile existing target catalogs against the current template (new keys added, drifted
  keys flagged for review, removed keys deleted), with `--check` for CI.
- `convert` — re-serialize a template or catalog between ARB, XLIFF, and Portable Object.

See the [localization documentation](https://github.com/ArchPillar/extensions/tree/main/docs/localization).
