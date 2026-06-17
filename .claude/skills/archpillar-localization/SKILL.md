---
name: archpillar-localization
description: >-
  Use when working on UI translation or localization in a .NET project — adding, editing, or
  reviewing translatable strings, ICU MessageFormat defaults, plural/gender handling, the
  ILocalizer / Localized string bundles, the `dotnet apl` tooling, or DI / IStringLocalizer /
  ASP.NET DataAnnotations integration — whether ArchPillar.Extensions.Localization is already
  referenced or you are choosing, introducing, or migrating a localization approach. Applies any
  time you would otherwise reach for .resx, IStringLocalizer, or string concatenation for plurals
  or gender.
---

# ArchPillar.Extensions.Localization

A UI translation library where **every translatable string is written once, at the call site, as an
in-code ICU MessageFormat default**. That default is both the source-language text and the terminal
fallback, so the app is fully functional with zero catalogs. A Roslyn generator extracts the call
sites at compile time; translators fill in per-language catalogs; at runtime those catalogs load as
**pluggable overrides** over the in-code default. It is deliberately the opposite of the
`.resx` / `IStringLocalizer` model: no separate resource files to author, no "missing resource"
state, no name-based lookup.

## The mental model (read this first)

**The everyday surface is tiny.** You write against exactly three entry points —
**`Localizer`** (ambient/static `Translate`), **`ILocalizer<T>`** (category-scoped), and
**`Localized<T>`** (a typed bundle) — plus **ICU MessageFormat written inside the default string**.
Everything else (the `dotnet apl` tooling, DI wiring, container formats, `IStringLocalizer`
interop) is one-time setup or migration, not something you touch when adding a string.

- The core call is **`Translate(key, default, …args)`**: a stable symbolic **key**, the **in-code
  ICU default** (source text + fallback), then `(name, value)` argument pairs for the placeholders.
- It resolves the loaded override for `CurrentUICulture` (walking parent cultures), and on any miss
  renders the in-code default. **A lookup never fails** — there is only "no override yet".
- Reach a localizer three ways: receiver-less **ambient** (`using static …Localizer;` →
  `Translate(...)`), an injected **`ILocalizer<T>`** (the `ILogger<T>` model — `T`'s full name is
  the key **category**), or a **`Localized<T>`** bundle where the member name is the key.
- All three feed **one process-wide ambient store** — an injected `ILocalizer<T>` and a static
  `Translate(...)` in an exception path resolve from the same catalogs configured once.

## Rules that are easy to get wrong — follow these exactly

These are the habits to unlearn coming from `.resx` / `IStringLocalizer` / `string.Format`.

1. **Author strings as in-code defaults, not resource files.** New translatable text is a
   `Translate(key, "Default text")` call — never a new `.resx` entry. `.resx` / `IStringLocalizer`
   exist only as a **migration interop** layer (see `references/di-runtime-and-interop.md`).
2. **Key and default must be compile-time constants.** A non-constant key or default is an error
   (`APL0001`). Put runtime data in **ICU placeholders** (`"Hello {name}"`), never in string
   interpolation or concatenation — `$"Hello {name}"` defeats extraction and is not translatable.
3. **Plurals and gender use ICU, not branching.** Write
   `"{count, plural, one {# item} other {# items}}"`, not `if (n==1) …` or string concat. Every
   `plural`/`select`/`selectordinal` must have an **`other`** branch (`APL0005`). Plural categories
   resolve against the **target culture** via CLDR — do not hardcode English one/other rules.
4. **Do not manage namespaces — scope by category.** Inject `ILocalizer<T>`; `T`'s type name is the
   category, so two components can both use `"title"` without colliding. No key prefixes, no
   central registry. Shared strings live on a shared type used as the scope.
5. **Catalogs are generated, never hand-authored.** `dotnet apl` produces and reconciles the
   per-language files from your code. Do not write XLIFF/ARB/PO by hand.
6. **Extraction needs a recent SDK.** The generator/analyzer require **.NET SDK 9.0.3xx+** (any
   .NET 10 SDK). On an older SDK the package still restores and runs, but extraction and the `APL`
   diagnostics **silently do nothing** — if keys aren't extracted, check `dotnet --version` first.
   (This is independent of the target framework; `net8.0` is fine.)
7. **Custom wrapper APIs need the attributes.** Detection is attribute-driven, not name-based. If
   you write your own method that forwards a translatable string, mark the parameter
   `[Translatable]` / `[TranslationDefault]` so the generator and analyzer recognize it.

## Canonical example

```csharp
using static ArchPillar.Extensions.Localization.Localizer;

// Ambient, no DI — reachable anywhere, even before a container exists.
string Greet(string name) => Translate("greeting", "Hello {name}!", ("name", name));

string Inbox(int count) => Translate(
    "inbox",
    "You have {count, plural, =0 {no messages} one {# message} other {# messages}}",
    ("count", count));
```

```csharp
// Category-scoped via DI (the ILogger<T> model): keys live under "…Checkout".
public sealed class Checkout(ILocalizer<Checkout> localizer)
{
    public string Pay => localizer.Translate("pay", "Pay now");
    public string Post => localizer.Translate("post", "Post", context: "menu"); // context disambiguates
}

// A bundle of fixed labels: member name = key, deriving type = category.
public sealed partial class ButtonLabels : Localized<ButtonLabels> // 'partial' lets DI ctor be generated
{
    public string Save   => Translate("Save");
    public string Cancel => Translate("Cancel");
}
```

Generate and reconcile translator files (run from the app folder; it finds the solution/project):

```bash
dotnet build                                  # also auto-extracts {Assembly}.en.xliff into Translations/
dotnet apl add de --output Translations       # create a German file, every entry NeedsTranslation
dotnet apl sync --output Translations          # reconcile all languages after code changes (--check in CI)
```

## Feature cheat-sheet

| Need | API / command | Notes |
| --- | --- | --- |
| Translate a string | `Translate(key, default, (name, value)…)` | Or instance `loc.Translate(...)` / indexer `loc[key, default]` |
| No-DI / static access | `using static …Localizer;` → `Translate(...)` | Ambient store; also `Localizer.Default`, `Localizer.For<T>()` |
| Scope keys | inject `ILocalizer<T>` | `T`'s full name is the category; no namespaces to manage |
| Disambiguate same key | `Translate(key, default, context: "menu")` | Keeps two meanings of one key/text separate |
| Bundle of fixed labels | `class X : Localized<X>` (`partial` for DI) | Member name = key; typo = compile error |
| Plural / gender | ICU `{n, plural, one {…} other {…}}`, `select`, `selectordinal` | Needs `other`; resolves by target-culture CLDR |
| Mark a non-localizer string | `L("text")` (`using static …TranslationMarkers;`) | Extracts an exception/log literal; no runtime change |
| Configure | `Localizer.Configure(new LocalizerOptions { SourceCulture = "en", TranslationsDirectory = "Translations" })` | One options surface; `Initialize(opts, eager:true)` to load at startup |
| DI registration | `services.AddArchPillarLocalization(options)` | `…DependencyInjection` package; chain `.AddArchPillarLocalizedBundles()` |
| Isolated / test scope | `new LocalizationContext(options)` or `Localizer.Reset()` | Context shares nothing with the ambient store |
| IStringLocalizer interop | `services.AddArchPillarStringLocalizer(options)` | `…StringLocalizer` package; composes over existing `.resx` |

## Packages

The core `ArchPillar.Extensions.Localization` package carries the runtime **and** activates the
analyzer + generator on reference — no setup. The only packages you reference directly are these
opt-in companions:

| Package | Add it for |
| --- | --- |
| `…Localization.Tooling` (global tool `dotnet apl`) | Generating/reconciling/exporting catalogs |
| `…Localization.DependencyInjection` | `AddArchPillarLocalization`, injected `ILocalizer<T>`, bundle registration |
| `…Localization.StringLocalizer` | `IStringLocalizer` interop + `.resx` migration on-ramp |
| `…Localization.AspNetCore` | `AddArchPillarDataAnnotationsLocalization` (MVC/Razor DataAnnotations) |

> `…Localization.Abstractions`, `…Localization.MessageFormat` (the ICU engine), `…Localization.Analyzers`,
> and `…Localization.CodeFixes` are **supporting libraries** pulled in automatically by the core package
> — you do not reference them directly. (The ICU engine is technically usable standalone, but that is a
> niche case, not part of normal localization work.)

## Deeper guidance

- [`references/tooling-and-deployment.md`](references/tooling-and-deployment.md) — full `dotnet apl`
  lifecycle (`status`/`extract`/`add`/`sync`/`export`/`import`/`merge`), formats (XLIFF/ARB/PO),
  CI gating, files-vs-embedded delivery, and the trim/NativeAOT matrix.
- [`references/di-runtime-and-interop.md`](references/di-runtime-and-interop.md) — DI registration,
  `Localized<T>` bundle wiring, the ambient store, `LocalizationContext` / `DefaultLocalizer`, hot
  reload, pseudo-localization, test isolation, plus `IStringLocalizer` interop & migration, the
  `L(...)` marker, and DataAnnotations / enum display localization.
- [`references/messageformat-and-diagnostics.md`](references/messageformat-and-diagnostics.md) — the
  ICU grammar surface, missing-argument policy, and the full `APL0001`–`APL0010` diagnostic table.
- Full docs: `docs/localization/` (`getting-started.md`, `features.md`, `translation-workflow.md`,
  `recommendations.md`) and `internals/SPEC.md`, published via Context7 (`archpillar/extensions`).
