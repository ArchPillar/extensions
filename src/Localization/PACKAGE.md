# ArchPillar.Extensions.Localization

A user-interface translation library for .NET. Author your source strings inline as ICU
MessageFormat, hand them to translators as standard catalog files (ARB by default; XLIFF and
Portable Object also supported), and load translations at runtime as pluggable overrides.

The in-code default message is always the source of truth for the source language and the terminal
fallback for every other language — so an app with **zero** translation files runs correctly, and
partial translations degrade gracefully key-by-key.

```csharp
var localizer = new Localizer(new LocalizerOptions { TranslationsDirectory = "Translations" });

// Renders the loaded de override, or falls back to the in-code default.
localizer.Translate(CultureInfo.GetCultureInfo("de"),
    "home.greeting", "Hello {name}", context: null, ("name", "Ada"));
```

Bundles the container-format providers; no dependencies beyond the Base Class Library. See the
[localization documentation](https://github.com/ArchPillar/extensions/tree/main/docs/localization).
