# Features

Every feature of the library, ordered roughly from the everyday path to the advanced. For the design
rationale behind any of these, see [internals/SPEC.md](internals/SPEC.md) and the numbered specs.

## The native localizer API

`ILocalizer.Translate(key, default, …)` is the core call. The first argument is a stable symbolic
**key**; the second is the **in-code default** in ICU MessageFormat — the source-of-truth text and
the terminal fallback. A lookup resolves the loaded override for `CurrentUICulture`, falling back
through parent cultures to the default.

```csharp
string title = localizer.Translate("home.title", "Home");
string greet = localizer.Translate("greeting", "Hello {name}", ("name", "Ada"));
string menu  = localizer.Translate("post", "Post", context: "menu"); // context disambiguates same key
```

## Categories — the `ILogger<T>` model

There are no user-managed namespaces. Every key is implicitly scoped by a **category** equal to the
full type name of `T` in `ILocalizer<T>`, exactly as `ILogger<T>` scopes log entries. Inject
`ILocalizer<MyComponent>` and its keys live under `MyComponent`, never colliding with another
component's identical key. Shared strings live in their own type used as the scope.

```csharp
public sealed class Checkout(ILocalizer<Checkout> localizer)
{
    public string Pay => localizer.Translate("pay", "Pay now"); // category = ...Checkout
}
```

## `Localized<TSelf>` — a bundle of strings

An optional base class for a set of related strings where the **member name is the key** (via
`[CallerMemberName]`) and the deriving type is the category, so neither is repeated:

```csharp
public sealed class ButtonLabels(ILocalizer<ButtonLabels> loc) : Localized<ButtonLabels>(loc)
{
    public string Save   => Translate("Save");
    public string Cancel => Translate("Cancel");
}
```

## The ambient store

Translations live in one process-wide, layered store modeled on `IConfiguration`, reachable with no
services — so a string localizes from anywhere, including an exception thrown before any container
exists. Read it through `Localization.Default` (global category) or `Localization.For<T>()`. For the
global category there is also a static `Localization.Translate`: add `using static
ArchPillar.Extensions.Localization.Localization;` and call `Translate(...)` with no receiver, the way
`using static System.Console;` gives you `WriteLine(...)`.

```csharp
string s = Localization.Default.Translate("home.title", "Home");
string t = Translate("home.title", "Home");             // with `using static …Localization;` — the same call
Localization.AddCatalog(catalog);                       // layer a host override (last source wins)
Localization.SourceCulture = "en";                      // language the in-code defaults are written in
Localization.TranslationsDirectory = "Translations";    // where loose files are read from
```

Sources layer **embedded < satellite < directory < host**, last-wins; a lookup is one lock-free read
that falls to the in-code default on a miss. `Localization.Reset()` clears everything (for test
isolation). See [recommendations.md](recommendations.md) for why the store is global and how to keep
tests deterministic against it.

## Loading — files, embedded, and satellites

A catalog reaches the store one of three ways. **Files-on-disk is the default** and the one path that
works under every publish mode:

- **Files (default).** Each library's catalogs copy to the output as
  `Translations/<AssemblyName>.<culture>.<ext>`; the store loads `TranslationsDirectory` on first use.
- **Embedded (opt-in, `ArchPillarLocalizationEmbedTargets=true`).** Catalogs become standard culture
  **satellite assemblies**, discovered lazily per requested culture; a culture-neutral or merged
  catalog can ride in the main assembly via `[LocalizationCatalog]`.

> Trimming, single-file, and NativeAOT behave differently for embedded catalogs — see the matrix in
> [recommendations.md](recommendations.md). The files path is safe everywhere.

## ICU MessageFormat and plurals

Defaults and translations are written in ICU MessageFormat: arguments, `{name, number|date|time, style}`,
`plural` / `selectordinal` (with `offset`, `=N` selectors, and `#`), `select`, and nesting. Plural
categories resolve from embedded Unicode CLDR data against the **target** culture, so one template
pluralizes correctly per language.

```csharp
localizer.Translate("inbox",
    "You have {count, plural, =0 {no messages} one {# message} other {# messages}}", ("count", 5));
// "You have 5 messages"
```

The grammar is implemented by `ArchPillar.Extensions.Localization.MessageFormat`, a dependency-free
package usable on its own (`MessageFormatter.Format`, `MessageSyntax.TryValidate` /
`ExtractPlaceholders`, `PluralRules`). By default a missing argument renders its placeholder unchanged
and never throws; opt into `MissingArgumentPolicy.Throw` to fail instead.

## Container formats

Catalogs round-trip through three standard formats, all bundled into the runtime (no separate
packages): **ARB** (the default, JSON-based), **XLIFF 2.1**, and **Portable Object** (gettext). The
runtime loads all three; on a per-key overlap the higher-fidelity format wins (`xliff` > `arb` > `po`).
Each provider (`ArbTranslationFormat`, `XliffTranslationFormat`, `PoTranslationFormat`) is public and
stream-based, so you can load a catalog from any source — a file, an embedded resource, an HTTP
response, or a database.

## Compile-time extraction and the typed key registry

A Roslyn source generator extracts every attributed call site into a source-language template on a
real build (never at design time), and emits a strongly-typed key registry so call sites and the
analyzer share rename-safe keys. The shared analyzer surfaces, in the editor, what would otherwise be
a silent bug:

| Diagnostic | Meaning |
|------------|---------|
| `APL0001` | A translatable key/default is not a compile-time constant (error). |
| `APL0002` | The default is not valid ICU MessageFormat. |
| `APL0003` / `APL0004` | A placeholder has no argument / an argument is unused. |
| `APL0005` | A `plural`/`select` is missing its `other` branch. |
| `APL0006` / `APL0007` | A duplicate key with conflicting text / identical text under different keys. |
| `APL0008` | A key does not match the configured pattern. |

The `dotnet apl` tool turns the emitted template into per-language files (`add`, `sync`, `convert`,
`sync --check` as a CI gate) and merges them at publish time. Nothing touches a translator's files as
a build side effect.

## Dependency injection

`AddArchPillarLocalization` (in the `…Localization.DependencyInjection` package) feeds the ambient
store from `LocalizerOptions` and registers the native views — `ILocalizer`, `ILocalizer<T>`, and a
concrete `Localizer`:

```csharp
services.AddArchPillarLocalization(new LocalizerOptions { TranslationsDirectory = "Translations", SourceCulture = "en" });
```

No extra wiring is needed for request culture — the localizers read `CurrentUICulture`, which
`app.UseRequestLocalization(...)` sets per request. This package depends only on the DI abstractions;
`IStringLocalizer` interop lives in a separate package (below).

## IStringLocalizer interop

For existing code, add the separate `…Localization.StringLocalizer` package and call
`AddArchPillarStringLocalizer` (it performs the native registration above and adds the adapters). It
exposes the store as `IStringLocalizer` / `IStringLocalizer<T>`: the name is the key, the category is
`typeof(T)`, and positional arguments map to `{0}`, `{1}`, … Critically it **composes** — it registers the
`.resx` factory itself and **falls through to it on an ambient miss**, so existing `.resx` keeps resolving
regardless of whether you call `AddLocalization()` before or after it. Because this is the framework's
single `IStringLocalizerFactory` seam, MVC `IViewLocalizer`/`IHtmlLocalizer` and
`AddDataAnnotationsLocalization` resolve through it too.

```csharp
services.AddArchPillarStringLocalizer(new LocalizerOptions { SourceCulture = "en" });
// ...
public sealed class LegacyModel(IStringLocalizer<LegacyModel> loc)
{
    public string Title => loc["Home"];
    public string Inbox(int n) => loc["You have {0}", n];
}
```

## Migration on-ramp

Adopting the library next to an existing `IStringLocalizer` / `.resx` codebase costs almost nothing, and the
interop package is meant to be dropped once you no longer depend on `IStringLocalizer`:

- **Existing translations keep working** via the composing adapter above.
- **`IStringLocalizer` indexer literals are extracted automatically** (on by default): the literal is
  both key and default under `typeof(T)`'s category. Only constant, valid-ICU literals are taken; a
  dynamic key or a `string.Format`-style literal (`"{0:C}"`) is skipped silently, so a build never breaks.
- **`L(...)` marks anything else** — a log line, a `throw new(...)` message — for extraction without
  changing runtime behavior:

  ```csharp
  using static ArchPillar.Extensions.Localization.TranslationMarkers;
  throw new ArgumentException(L("Email is required"));
  ```

> `.resx` keys, DataAnnotations messages, and view-localization calls are **not** extracted (they have
> no in-code default to harvest); the adapter still serves them at runtime. See
> [recommendations.md](recommendations.md) for the migration ordering.

## Publishing — merge per culture

For a clean deployment, merge the per-library files into one bundle per culture at publish time:

```bash
dotnet apl merge --input <dir> --output <dir> --format arb
```

This runs automatically on `dotnet publish` (`ArchPillarLocalizationMergeOnPublish`, default on). The
merge reuses the runtime's own load, so a merged bundle resolves identically to the many-files path.

## Pseudo-localization

Layer a `PseudoLocalizationSource` to pseudo-translate every string (accented, length-expanded) while
preserving ICU placeholders — a quick way to spot un-extracted strings and layout that breaks under
longer text:

```csharp
Localization.AddSource(new PseudoLocalizationSource());
```

## Hot reload

An isolated `Localizer` can watch its directory and reload on change (`EnableHotReload`, debounced).
The rebuild swaps an immutable snapshot atomically, so concurrent `Translate` calls never tear.

## Isolated localizers

For tests or multi-tenant scenarios, construct a `Localizer` that bypasses the ambient store and reads
only the catalogs you hand it (`Localizer.FromCatalogs(...)` for hosts without a file system, such as
Blazor WebAssembly — fetch and parse the catalogs, then hand them in):

```csharp
using var localizer = new Localizer(catalogs, new LocalizerOptions { SourceCulture = "en" });
```
