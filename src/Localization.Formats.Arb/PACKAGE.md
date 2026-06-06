# ArchPillar.Extensions.Localization.Formats.Arb

The Application Resource Bundle (ARB) container-format provider for the
`ArchPillar.Extensions.Localization` family. ARB is a JSON-based, ICU-native localization format
(used by Flutter and readable in Poedit), and is the family's default authoring format because the
symbolic key maps directly to the JSON key.

Implements `ITranslationFormat` over `System.Text.Json` (BCL) with byte-stable, deterministic output.
See the [localization documentation](https://github.com/ArchPillar/extensions/tree/main/docs/localization).
