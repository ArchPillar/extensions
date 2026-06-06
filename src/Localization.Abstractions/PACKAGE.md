# ArchPillar.Extensions.Localization.Abstractions

Shared abstractions for the `ArchPillar.Extensions.Localization` family, depending on nothing
beyond the Base Class Library:

- The `[Translatable]` / `[TranslationDefault]` / `[TranslationContext]` / `[TranslationComment]`
  attributes that mark a translatable call.
- The format-neutral `Catalog` / `CatalogEntry` model and `TranslationState`.
- The `ITranslationFormat` provider interface and `FormatCapabilities`.
- The `TranslationFormatRegistry` for resolving providers by id or file extension.

See the [localization documentation](https://github.com/ArchPillar/extensions/tree/main/docs/localization).
