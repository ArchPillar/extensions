# Features

Every feature of the library, ordered roughly from the everyday path to the advanced. For the design
rationale behind any of these, see [internals/SPEC.md](internals/SPEC.md) and the numbered specs.

## The native localizer API

`ILocalizer.Translate(key, default, …)` is the core call, and the one contract everything else is a
view over. The first argument is a stable symbolic **key** — an identifier that never changes even as
the wording does, so a translation survives a copy edit. The second is the **in-code default** in ICU
MessageFormat: this is the source-of-truth text, the string a reader sees when no catalog is loaded,
and the terminal fallback when a lookup misses. A lookup resolves the loaded override for
`CurrentUICulture`, walking up the parent-culture chain (`de-AT` → `de` → invariant) and, on a miss at
every level, rendering the default. Because the default lives at the call site, the code is readable on
its own and there is never a "missing resource" state — only "no override yet".

```csharp
string title = localizer.Translate("home.title", "Home");
string greet = localizer.Translate("greeting", "Hello {name}", ("name", "Ada"));
string menu  = localizer.Translate("post", "Post", context: "menu"); // context disambiguates same key
```

The optional **context** is a disambiguator: the same key and default can mean different things in
different places (a "Post" button versus a blog "Post"), and a distinct `context` keeps their
translations separate without inventing two keys.

The identical lookup is also exposed as an **indexer**, for callers who prefer the `IStringLocalizer`
shape — `loc["key", "default"]`, with arguments passed as a `(name, value)` array. It is purely a
matter of taste; the analyzer and extractor treat both the same. Settle on one style per codebase (see
[getting started](getting-started.md#2-translate-a-string)).

```csharp
string title = localizer["home.title", "Home"];
string greet = localizer["greeting", "Hello {name}", [("name", "Ada")]];
```

## Categories — the `ILogger<T>` model

There are no user-managed namespaces to design, register, or keep unique. Every key is implicitly
scoped by a **category** equal to the full type name of `T` in `ILocalizer<T>`, exactly as `ILogger<T>`
scopes log entries by the logging type. Inject `ILocalizer<MyComponent>` and its keys live under
`MyComponent`, so two components can both use `"title"` without ever colliding — the category keeps
them apart, and a rename of the type moves the whole bucket with it. The receiver-less global category
(`Localizer.Default`, the static `Translate`) is just the empty category, fine for an app's top level
but worth scoping as soon as more than one component owns strings.

```csharp
public sealed class Checkout(ILocalizer<Checkout> localizer)
{
    public string Pay => localizer.Translate("pay", "Pay now"); // category = ...Checkout
}
```

**Shared strings** ("OK", "Cancel", "Loading…") are not a special feature: put them on a type and use
that type as the scope — inject `ILocalizer<SharedResource>` wherever you need them. The shared type is
ordinary code reuse that doubles as the category, with no central registry to maintain.

## `Localized<TSelf>` — a bundle of strings

An optional base class for a set of related strings where the **member name is the key** (captured via
`[CallerMemberName]`) and the deriving type is the category, so you repeat neither. It turns a group of
labels into a small strongly-typed surface: callers get `labels.Save` instead of a stringly-typed
`Translate("Save", "Save")`, and a typo in a member name is a compile error rather than a silent new
key. Reach for it when a component has a handful of fixed labels; stay with plain `Translate(...)` when
keys are dynamic or there is only one.

A bundle needs an `ILocalizer<TSelf>`, and how it gets one is the only difference between the two ways to
use it — pick by how the rest of your app is wired.

**Ambient (no DI).** Declare no constructor and the bundle resolves itself from the ambient store, so a
bare `new` needs no services and no registration — the fit for a console app or a script:

```csharp
public sealed class ButtonLabels : Localized<ButtonLabels>
{
    public string Save   => Translate("Save");   // key "Save", category ...ButtonLabels
    public string Cancel => Translate("Cancel");
}

var labels = new ButtonLabels();                 // reads the ambient store
```

**Dependency injection.** Mark the bundle `partial` and the generator writes its constructors for you — an
ambient one and an `ILocalizer<TSelf>` one for the container — so you inject the bundle with no hand-written
plumbing. You do not register each one either: the generator also emits an `AddArchPillarLocalizedBundles()`
extension covering every bundle in the assembly, so a single call wires them all:

```csharp
public sealed partial class ButtonLabels : Localized<ButtonLabels>
{
    public string Save   => Translate("Save");
    public string Cancel => Translate("Cancel");
}

builder.Services.AddArchPillarLocalization(options).AddArchPillarLocalizedBundles();
// ButtonLabels is now injectable wherever you need it.
```

To write the constructor yourself — to take extra dependencies, say — declare the `ILocalizer<TSelf>` one
and the generator leaves it alone: `public sealed class ButtonLabels(ILocalizer<ButtonLabels> loc) :
Localized<ButtonLabels>(loc)`.

> `AddArchPillarLocalizedBundles()` registers every bundle
> with an accessible `ILocalizer<TSelf>` constructor — hand-written or generated — as a singleton; it is
> generated only when the project references the DI package, and is `internal`, so a library that exposes
> bundles registers its own. When DI is referenced, analyzer `APL0010` flags a non-`partial`, constructor-less
> bundle and offers a one-click fix to mark it `partial`.

## The ambient store

Translations live in one process-wide, layered store modeled on `IConfiguration`, reachable with no
services — so a string localizes from anywhere, including an exception thrown before any container
exists. This is what removes the usual chicken-and-egg of localization: there is no factory to resolve
and no constructor to thread, so even code that runs before (or entirely without) a DI container still
gets translated text. Read it through `Localizer.Default` (the global category) or `Localizer.For<T>()`.
For the global category there is also a static `Localizer.Translate`: add `using static
ArchPillar.Extensions.Localization.Localizer;` and call `Translate(...)` with no receiver, the way
`using static System.Console;` gives you `WriteLine(...)`.

Configuration goes through the `LocalizerOptions` object — there is **one** configuration surface, not a
scatter of mutable knobs. `Localizer.Configure(options)` applies it in a single rebuild; `Initialize`
does the same and can eager-load up front (otherwise the store loads lazily on first use):

```csharp
string s = Localizer.Default.Translate("home.title", "Home");
string t = Translate("home.title", "Home");          // with `using static …Localizer;` — the same call
Localizer.AddCatalog(catalog);                       // layer a host override (last source wins)
Localizer.Configure(new LocalizerOptions            // the single configuration surface
{
    SourceCulture = "en",                            // language the in-code defaults are written in
    TranslationsDirectory = "Translations"           // where loose files are read from
});
Localizer.Initialize(options, eager: true);          // configure and load now, at startup
```

Sources layer **embedded < satellite < directory < host**, last-wins; a lookup is one lock-free read
that falls to the in-code default on a miss. Internally the loaded catalogs are merged into a single
flat snapshot, and any custom `ITranslationSource` layers sit above it as additional, equally-ranked
layers resolved by the very same loop — there is no privileged path for either (see
[the loading model](internals/SPEC.md)). `Localizer.Reset()` clears everything back to empty (for test
isolation). See [recommendations.md](recommendations.md) for why the store is global and how to keep
tests deterministic against it.

## The localization context

The ambient store is convenient, but a process-wide static is not always wanted — parallel tests would
bleed into each other, a single process might host more than one localization scope, and some teams
forbid static mutable state on principle. A **`LocalizationContext`** is the answer: the same
environment the ambient facade wraps, exposed as an ordinary object you can construct, configure, and
dispose. In fact the ambient `Localizer` is *exactly* one of these, held in a single static field.

```csharp
using var context = new LocalizationContext(new LocalizerOptions { SourceCulture = "en" });
context.AddCatalog(catalog);

string s = context.Default.Translate("home.title", "Home");
string t = context.For<Checkout>().Translate("pay", "Pay now");
```

A constructed context shares nothing with the ambient one or with any other context — two of them never
see each other's catalogs — which is what makes them safe for test isolation and multi-scope hosting. It
carries the full call and configuration surface (`Default`, `For<T>()`, `Translate`, `AddCatalog`,
`AddSource`, `Configure`, `Load`, `Reset`), and disposing it tears down its directory watcher. For an
isolated environment, construct one directly and thread it through your own code rather than reaching for
the static `Localizer`.

## Loading — files, embedded, and satellites

How a catalog physically reaches the store is independent of how you read from it — the resolution API
is the same whichever delivery mechanism you choose. **Files-on-disk is the default** and the one path
that works under every publish mode, so unless you have a single-file or AOT constraint there is nothing
to decide here:

- **Files (default).** Each library's catalogs copy to the output as
  `Translations/<AssemblyName>.<culture>.<ext>`; the store reads `TranslationsDirectory` on first use,
  and (with hot reload on) watches it for changes. Naming each file by assembly is what lets independent
  libraries ship translations without colliding.
- **Embedded (opt-in, `ArchPillarLocalizationEmbedTargets=true`).** Catalogs become standard culture
  **satellite assemblies**, discovered lazily the first time a culture is requested — you pay nothing for
  cultures you never select. A culture-neutral or merged catalog can instead ride inside the main
  assembly via `[LocalizationCatalog]`, which is the AOT-safe embed.

Satellite discovery hooks `AssemblyLoad`, so a catalog in a library that loads later is picked up
automatically; there is no manifest to keep in sync. Whichever mechanism a given assembly uses, its
catalogs merge into the same layered store and resolve identically.

> Trimming, single-file, and NativeAOT behave differently for embedded catalogs — see the matrix in
> [recommendations.md](recommendations.md). The files path is safe everywhere.

## ICU MessageFormat and plurals

Defaults and translations are written in **ICU MessageFormat**, the same grammar `.po`/`.arb` translators
already use, so a string carries its own grammar rather than relying on string concatenation that breaks
in other languages. The full surface is supported: simple arguments, typed formatting
(`{name, number|date|time, style}`), `plural` / `selectordinal` (with `offset`, `=N` exact-match
selectors, and `#` for the formatted count), `select` for arbitrary categories, and arbitrary nesting of
all of these. Crucially, **plural categories resolve against the target culture** from embedded Unicode
CLDR data — so the one template below pluralises by English rules under `en`, by Polish rules (which has
`one`/`few`/`many`/`other`) under `pl`, and so on, with no per-language code.

```csharp
localizer.Translate("inbox",
    "You have {count, plural, =0 {no messages} one {# message} other {# messages}}", ("count", 5));
// "You have 5 messages"
```

The grammar is implemented by `ArchPillar.Extensions.Localization.MessageFormat`, a dependency-free
package usable entirely on its own — `MessageFormatter.Format` to render, `MessageSyntax.TryValidate` /
`ExtractPlaceholders` to lint and inspect a template, `PluralRules` for the raw CLDR categories. By
default a referenced argument with no supplied value renders its placeholder unchanged and never throws
(so a partial call still produces readable output); switch `MissingArgumentPolicy.Throw` in the options
to fail fast instead.

## Container formats

Catalogs round-trip through three standard, translator-tooling-friendly formats, all bundled into the
runtime (no separate packages, no plugin to register): **ARB** (the default, a JSON dialect with rich
metadata), **XLIFF 2.1** (the XML interchange standard most TMS tools speak), and **Portable Object**
(gettext `.po`). You author in whichever your translation pipeline prefers and the runtime loads all
three side by side; when the same culture and key appear in more than one file the higher-fidelity
format wins (`xliff` > `arb` > `po`, configurable via `FormatPrecedence`), so a richer XLIFF entry is
never shadowed by a leaner `.po` one. Each provider (`ArbTranslationFormat`, `XliffTranslationFormat`,
`PoTranslationFormat`) is public and **stream-based**, so a catalog can come from anywhere — a file, an
embedded resource, an HTTP response, a database column — and you can build a custom `ITranslationSource`
on top of one.

## Compile-time extraction and the typed key registry

A Roslyn source generator extracts every translatable call site into a source-language template on a
real build (never at design time, so editing never churns files), and emits a strongly-typed key
registry so call sites and the analyzer share rename-safe keys. "Translatable call site" is not a name
match — it is driven by the `[Translatable]` / `[TranslationDefault]` parameter attributes on the API,
which is why `Translate(...)`, the indexer, an `L(...)` marker, and even your own wrapper methods are all
recognised the same way. The shared analyzer then surfaces, in the editor as you type, what would
otherwise be a silent runtime bug:

| Diagnostic | Meaning |
|------------|---------|
| `APL0001` | A translatable key/default is not a compile-time constant (error). |
| `APL0002` | The default is not valid ICU MessageFormat. |
| `APL0003` / `APL0004` | A placeholder has no argument / an argument is unused. |
| `APL0005` | A `plural`/`select` is missing its `other` branch. |
| `APL0006` / `APL0007` | A duplicate key with conflicting text / identical text under different keys. |
| `APL0008` | A key does not match the configured pattern. |
| `APL0010` | A DI consumer's `Localized<>` bundle is not `partial`, so its constructor and registration cannot be generated (one-click fix marks it `partial`). |

The `dotnet apl` tool turns the emitted template into per-language files (`add`, `sync`, `convert`,
`sync --check` as a CI gate) and merges them at publish time. Nothing touches a translator's files as
a build side effect.

## Display annotations — DataAnnotations and enums

`[DisplayName]`, `[Display(Name = …)]`, `[Display(Description = …)]`, and `[Description]` carry genuine
display text that ASP.NET model metadata and other reflection consumers render, so the extractor lifts
them into the template **by default** — the system attribute's literal becomes both the key and the
in-code default, scoped to the declaring type's category, the same text-as-key the framework already
looks up by. There is no call site to write and no `L(...)` to add: annotate as you already do and the
strings reach translators. Opt a project out with `ArchPillarLocalizationExtractAnnotations=false`.

Because an attribute argument must be a compile-time constant, the key *is* the literal — which drifts
if you later fix a typo. A **twin attribute** removes that coupling: pair the system attribute with a
`[Localized…]` twin carrying a stable key and a clean default. The twin rides beside the system
attribute (it does not replace it), and extraction — and the integrations below — use the twin's key.

```csharp
public sealed class RegisterModel
{
    [Display(Name = "Email address")]                              // extracted under key "Email address"
    public string Email { get; set; } = "";

    [Display(Name = "Password")]
    [LocalizedDisplayName("register.password.label", "Password")]  // extracted under key "register.password.label"
    public string Password { get; set; } = "";
}
```

There is one twin per display concept — `[LocalizedDisplayName]` (for `[DisplayName]` and
`[Display(Name)]`) and `[LocalizedDescription]` (for `[Description]` and `[Display(Description)]`) — plus
one generic twin for the open-ended validation case, `[LocalizedMessage<TValidation>]`, keyed by the
validator type so a property carrying several validators stays unambiguous:

```csharp
[Required]
[StringLength(100)]
[LocalizedMessage<RequiredAttribute>("register.email.required", "An email address is required.")]
[LocalizedMessage<StringLengthAttribute>("register.email.tooLong", "That email is too long.")]
public string Email { get; set; } = "";
```

**Enums** read their own annotation at runtime: `value.GetLocalizedDisplayName()` reads the member's
`[LocalizedDisplayName]` twin (or its `[Display(Name)]` literal) and resolves it through the localizer
under the enum's category — the localized replacement for the usual hand-rolled `GetDisplayName()`.

```csharp
public enum AccountStatus
{
    [Display(Name = "Active")] Active,
    [LocalizedDisplayName("account.suspended", "Suspended")] Suspended,
}

string label = AccountStatus.Suspended.GetLocalizedDisplayName();   // resolves account.suspended
```

**ASP.NET MVC / Razor Pages** route their DataAnnotations through the localizer with one call on the MVC
builder (in the `…Localization.AspNetCore` package): display names and validation messages resolve under
the model type's category — by the literal as key, or, where a member has a twin, by the twin's stable key.

```csharp
builder.Services.AddControllersWithViews().AddArchPillarDataAnnotationsLocalization();
```

> Reading a member's attributes is reflection at runtime — inherent to attributes, which the rest of the
> library avoids. `[LocalizedMessage<T>]` is extracted to the catalog today, but routing a validator's
> stable key into the framework's own lookup needs .NET 11's `IValidationLocalizer` /
> `ErrorMessageKeyProvider` (Minimal APIs and Blazor's new validation); that seam is a follow-up.

## Dependency injection

`AddArchPillarLocalization` (in the `…Localization.DependencyInjection` package) configures a single
`LocalizationContext` from `LocalizerOptions` and registers the native views over it — `ILocalizer`,
`ILocalizer<T>`, and the concrete `DefaultLocalizer`:

```csharp
services.AddArchPillarLocalization(new LocalizerOptions { TranslationsDirectory = "Translations", SourceCulture = "en" });
```

DI feeds the **process-wide ambient context**, so an injected `ILocalizer<T>` and a receiver-less static
`Translate(...)` resolve from the same catalogs — you configure once and both worlds agree. For an isolated
environment (parallel test suites, multi-tenant hosting), construct a `LocalizationContext` directly and
thread it through your own code; see [the localization context](#the-localization-context) for the model.

For [`Localized<TSelf>`](#localizedtself--a-bundle-of-strings) bundles, chain the generated
`AddArchPillarLocalizedBundles()` after it. The generator emits that extension covering every bundle in the
assembly, registering each through its `ILocalizer<TSelf>` constructor as a singleton — so you inject bundles
instead of constructing them, with nothing to register by hand:

```csharp
services.AddArchPillarLocalization(options).AddArchPillarLocalizedBundles();
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

> `.resx` keys, a validator's `ErrorMessage`, and view-localization calls are **not** extracted (they
> have no in-code default to harvest); the adapter still serves them at runtime. Display **annotations**
> (`[DisplayName]` / `[Display]` / `[Description]`) are extracted separately — see
> [Display annotations](#display-annotations--dataannotations-and-enums). See
> [recommendations.md](recommendations.md) for the migration ordering.

## Publishing — merge per culture

For a clean deployment, merge the per-library files into one bundle per culture at publish time:

```bash
dotnet apl merge --input <dir> --output <dir> --format arb
```

This runs automatically on `dotnet publish` (`ArchPillarLocalizationMergeOnPublish`, default on). The
merge reuses the runtime's own load, so a merged bundle resolves identically to the many-files path.

## Pseudo-localization

Layer a `PseudoLocalizationSource` and every string is pseudo-translated — accented and
length-expanded (`[!!! Ḩéłłö !!!]`) while ICU placeholders are preserved intact. It is a fast,
language-free QA pass: any string that comes out in plain Latin was never extracted, and any layout that
clips or wraps badly will break under genuinely longer languages too. Because it is just another
`ITranslationSource`, it layers over real catalogs and is trivial to gate behind a build flag.

```csharp
Localizer.AddSource(new PseudoLocalizationSource());
```

## Hot reload

A `CatalogStore` can watch its directory and reload on change (`EnableHotReload`, debounced by
`HotReloadDebounce` so a flurry of saves coalesces into one reload). A `DefaultLocalizer` over the store
always resolves against the store's **latest** snapshot, swapped atomically, so concurrent `Translate`
calls never tear or block — an in-flight render finishes against the old snapshot and the next lookup
sees the new one. Edit a translation file and the running app reflects it without a restart.

```csharp
using var store = new CatalogStore(new LocalizerOptions { TranslationsDirectory = "Translations", EnableHotReload = true });
var localizer = new DefaultLocalizer(store);   // reads store.Snapshot live
```

## Isolated localizers

When you want a localizer that shares nothing with the ambient store, you have two levels. For a full
environment — its own configuration, directory, watcher, and the `For<T>()` / `AddCatalog` surface —
construct a [`LocalizationContext`](#the-localization-context). For just the resolution engine over a
fixed set of catalogs, construct a `DefaultLocalizer` directly: it bypasses the store entirely and reads
only the catalogs you hand it. `DefaultLocalizer.FromCatalogs(...)` is the convenience for hosts with no
file system, such as Blazor WebAssembly — fetch and parse the catalogs over HTTP, then hand them in.

```csharp
var localizer = new DefaultLocalizer(catalogs, new LocalizerOptions { SourceCulture = "en" });
```
