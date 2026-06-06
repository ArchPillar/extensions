# Runtime — usage

`ArchPillar.Extensions.Localization` is the package an application references. It loads translation
catalogs as **overrides** and renders translatable call sites; the in-code default is always the
source of truth for the source language and the terminal fallback for every other language, so an
app with no translation files still runs correctly.

> Design reference: [`05-runtime.md`](05-runtime.md). The container-format providers (ARB today;
> XLIFF and Portable Object next) are bundled into this package — there are no separate `Formats.*`
> packages.

## Translating

```csharp
using var localizer = new Localizer(new LocalizerOptions
{
    TranslationsDirectory = "Translations",
    SourceCulture = "en"
});

localizer.Translate(CultureInfo.GetCultureInfo("de"),
    "home.greeting", "Hello {name}", context: null, ("name", "Ada"));
```

The first two arguments are the **stable key** and the **in-code ICU default**. There are terser
overloads that use `CultureInfo.CurrentUICulture`:

```csharp
localizer.Translate("home.greeting", "Hello {name}", ("name", "Ada"));
localizer.Translate("post", "Post", context: "menu");   // context disambiguates same-key entries
```

## Resolution order

For `(culture, key, context)` the localizer tries, in order:

1. the override for the **exact** culture,
2. each **parent** culture (`de-AT` → `de` → invariant),
3. the **in-code default** supplied at the call site.

An **override** is formatted against the **requested culture** (a German override pluralizes by German
rules even though the key was authored in another language). The **in-code default** is formatted
against the **`SourceCulture`** instead — it is source-language text, so it must pluralize by the rules
of the language it was written in, not by whatever culture happened to be requested. The defaults are
never assumed to be English; `SourceCulture` (default `en`, fully configurable) declares the language
they are authored in. A missing snapshot, culture, or key never fails — it degrades to the default for
that one call.

## Loading

- Every catalog file in `TranslationsDirectory` is loaded and grouped by culture; formats may be
  mixed, and on per-key overlap the higher-fidelity format wins (`FormatPrecedence`, default
  `xliff` > `arb` > `po`).
- The **source-language** file is never loaded as an override (the in-code default wins).
- Untranslated / empty entries are skipped so they fall through to the default or a parent culture.
- A malformed file is skipped (it never crashes the app); the rest still load.

## Options

| Option | Default | Purpose |
|---|---|---|
| `TranslationsDirectory` | `Translations` beside the binary | where catalogs are loaded from |
| `SourceCulture` | `en` | the source language, excluded from overrides |
| `Cultures` | `null` (discover) | restrict the loaded cultures |
| `FormatPrecedence` | `["xliff","arb","po"]` | winner on cross-format overlap |
| `MissingArguments` | `PassThrough` | render `{name}` unchanged vs. throw |
| `EnableHotReload` | `false` | watch the directory and reload (debounced) |

`Reload()` rebuilds the snapshot and swaps it in atomically; concurrent `Translate` calls never see a
torn state. The localizer is thread-safe, `IDisposable`, and designed to be a singleton.
