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
scatter of mutable knobs. `Localizer.Initialize(options)` applies it in a single rebuild, and can
eager-load up front (otherwise the store loads lazily on first use):

```csharp
string s = Localizer.Default.Translate("home.title", "Home");
string t = Translate("home.title", "Home");          // with `using static …Localizer;` — the same call

var options = new LocalizerOptions                   // the single configuration surface
{
    SourceCulture = "en",                            // language the in-code defaults are written in
    TranslationsDirectory = "Translations"           // where loose files are read from
};
Localizer.Initialize(options);                       // configure now, load lazily on first use
Localizer.Initialize(options, eager: true);          // configure and load now, at startup

// Layer a host override: there is no runtime mutation surface, so build new options with an
// extra provider factory appended and reconfigure the ambient context (last provider wins on overlap).
Localizer.Ambient.Configure(options with { Providers = [.. options.Providers, _ => new InMemoryCatalogProvider([catalog])] });
```

Sources layer **embedded < satellite < directory < host**, last-wins; a lookup is one lock-free read
that falls to the in-code default on a miss. Internally the loaded catalogs from every provider — built-in
or a custom one added through `LocalizerOptions.Providers` — merge into a single flat snapshot resolved by
the very same loop, so a custom provider is never a second-class source (see
[the loading model](internals/SPEC.md)). `Localizer.Ambient.Reset()` clears everything back to empty (for test
isolation). See [recommendations.md](recommendations.md) for why the store is global and how to keep
tests deterministic against it.

## The localization context

The ambient store is convenient, but a process-wide static is not always wanted — parallel tests would
bleed into each other, a single process might host more than one localization scope, and some teams
forbid static mutable state on principle. A **`LocalizationContext`** is the answer: the same
environment the ambient facade wraps, exposed as an ordinary object you can construct, configure, and
dispose. In fact the ambient `Localizer` is *exactly* one of these, held in a single static field.

```csharp
var options = new LocalizerOptions { SourceCulture = "en" };
using var context = new LocalizationContext(options);
context.Configure(options with { Providers = [.. options.Providers, _ => new InMemoryCatalogProvider([catalog])] });

string s = context.Default.Translate("home.title", "Home");
string t = context.For<Checkout>().Translate("pay", "Pay now");
```

A constructed context shares nothing with the ambient one or with any other context — two of them never
see each other's catalogs — which is what makes them safe for test isolation and multi-scope hosting. It
carries the full call and configuration surface (`Default`, `For<T>()`, `Translate`, `Configure`, `Load`,
`LoadCultureAsync`, `PreloadAllAsync`, `Reset`), and disposing it tears down its directory watcher. For an
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
catalogs merge into the same layered store and resolve identically. Each of these delivery mechanisms is
implemented as a [catalog provider](#catalog-providers) behind a single interface.

> Trimming, single-file, and NativeAOT behave differently for embedded catalogs — see the matrix in
> [recommendations.md](recommendations.md). The files path is safe everywhere.

## Catalog providers

A **catalog provider** is the seam between *where catalog bytes come from* and *how the store reads them*.
Each delivery mechanism above is a provider implementing `ICatalogProvider`; the store owns the providers,
asks them what catalogs exist, and parses the bytes itself with the matching container format. This is the
one public extension point for a custom catalog source — there is no separate single-key-lookup mechanism;
every override, built-in or custom, arrives as a whole catalog.

**Discovery is split from load, and sealed into construction.** A provider is *born ready*: by the time you
hold an instance, its descriptor inventory is known and exposed **synchronously** — a synchronous provider
scans in its constructor (`new DirectoryCatalogProvider(dir)`), an asynchronous one does its async discovery
up front in a `static CreateAsync` (`await ManifestCatalogProvider.CreateAsync(httpClient, manifestUri)`).
The provider itself therefore has no asynchronous members:

```csharp
public interface ICatalogProvider
{
    IReadOnlyList<CatalogDescriptor> Catalogs { get; }
    IReadOnlyList<CatalogDescriptor> CatalogsFor(CultureInfo culture);
    IDisposable Watch(Action<CatalogDescriptor> onChanged);
}
```

`Catalogs` is everything the provider can enumerate cheaply, so the store learns which cultures exist.
`CatalogsFor` returns the catalogs for one exact culture and may surface descriptors `Catalogs` cannot —
a culture satellite is found only by probing for it (the store walks the parent chain itself). `Watch`
starts watching for change (a file edited under hot reload, an assembly loaded later) and invokes the
callback with the `CatalogDescriptor` that changed or newly appeared; it returns a handle that stops
watching when disposed, and a provider whose catalogs never change returns a no-op handle.

A provider never returns parsed catalogs. It returns `CatalogDescriptor`s — a culture, a format hint, an
optional name for diagnostics, and a `CatalogSource` opener — so listing what is available never reads any
bytes; the store opens a descriptor only when it decides to load it. **`CatalogSource` is a closed union**
that makes synchronous vs asynchronous loading a type-level distinction rather than a runtime guess:

```csharp
public abstract record CatalogSource
{
    public sealed record Synchronous(Func<Stream> Open) : CatalogSource;
    public sealed record Asynchronous(Func<CancellationToken, ValueTask<Stream>> OpenAsync) : CatalogSource;
}
```

Either arm hands the store a `Stream`; the parse (`ITranslationFormat.Read(Stream)`) is always synchronous.
The store decides what to do by pattern-matching the union — a `Synchronous` descriptor loads inline and
resolves immediately; an `Asynchronous` descriptor is never opened on the synchronous lookup path (that
would deadlock a single-threaded WebAssembly render), so it is awaited up front or loaded in the background.

Three providers ship in the box:

| Provider | Source | Load | `Watch` |
|----------|--------|------|---------|
| `DirectoryCatalogProvider` | Translation files under a directory (`File.OpenRead`). | `Synchronous` | Debounced `FileSystemWatcher`. |
| `ResourceCatalogProvider` | Main-assembly embedded `[LocalizationCatalog]` catalogs, plus culture satellites probed per culture. | `Synchronous` | `AppDomain.AssemblyLoad`. |
| `ManifestCatalogProvider` | An HTTP-served catalog manifest, each catalog fetched over `HttpClient`. | `Asynchronous` | No-op. |

The directory and resource providers' descriptors are `Synchronous`, which is what lets the store satisfy a
[live culture switch](#eager-vs-on-demand-culture-loading) straight from its synchronous lookup path, with
no blocking. The manifest provider's descriptors are `Asynchronous`: it genuinely awaits the network, so it
is loaded ahead of render (through `LoadCultureAsync` / `PreloadAllAsync`, or in the background on a
synchronous miss) rather than driving a synchronous switch — see the Blazor WebAssembly pattern in
[recommendations.md](recommendations.md).

### Choosing the providers

The store is **provider-agnostic**: it loads from an ordered list of providers, lowest-precedence-first (a
later provider wins on overlap), and never knows where the bytes come from. It auto-wires its synchronous
defaults, and a host layers further providers through `LocalizerOptions.Providers`:

| Store | Auto-default providers |
|-------|------------------------|
| Process-wide ambient (`Localizer` / `AddArchPillarLocalization`) | `[resource, directory]` — embedded and satellite catalogs beneath the directory, so app files win on overlap. |
| Explicit (`new LocalizationContext(options)`, or via DI) | `[directory]` — the directory provider alone, with no assembly discovery. |

There is **one way to add a provider** — build new options with an extra factory appended to `Providers`
and reconfigure — not a mutable `AddProvider` call; `LocalizationContext` has no runtime mutation surface.
`Providers` holds factories over the resolved options (`Func<LocalizerOptions, ICatalogProvider>`), so a
provider that needs no wiring is a trivial factory (`_ => provider`) and one that reads the configuration
does so at the moment it is built (`o => new MyProvider(o.Formats)`). A synchronous provider is `new`'d
inline inside the factory; an asynchronous one is `await CreateAsync`'d first (the `await` is visible at
the call site, because async loading is a real cost the reader should see), then wrapped as a trivial
factory. Every configured provider layers after the auto-defaults, in the order listed, and a later
provider wins on overlap:

```csharp
// Synchronous custom source (a database, an in-memory provider in tests).
var options = new LocalizerOptions
{
    Providers = [_ => new MyDatabaseCatalogProvider(connectionString)]
};
context.Configure(options);

// Asynchronous source — discover up front, then reconfigure with it appended.
var manifest = await ManifestCatalogProvider.CreateAsync(httpClient, "_content/app/translations.manifest.json");
context.Configure(options with { Providers = [.. options.Providers, _ => manifest] });
await context.LoadCultureAsync(CultureInfo.CurrentUICulture);   // awaited, no flash
```

> **`Configure` rebuilds the whole provider list from the options you pass — nothing is kept aside from
> them.** The directory a `DirectoryCatalogProvider` reads is set when the provider is built, and every
> `Configure` call (and so `Localizer.Initialize`) rebuilds the auto-default directory provider from the
> new `TranslationsDirectory`, and rebuilds `Providers` from the new options' list. To keep a provider you
> configured earlier across a reconfigure, carry it forward on the new options — `options with
> { TranslationsDirectory = ... }` keeps the same `Providers`; appending one more is `options with
> { Providers = [.. options.Providers, _ => anotherProvider] }`.

### Loading an asynchronous provider — `LoadCultureAsync`, `PreloadAllAsync`, `CatalogsChanged`

A synchronous lookup can only resolve what is already in memory, so an `Asynchronous` catalog is never
fetched on the lookup path. Three context/`Localizer` members drive asynchronous loading instead:

- **`LoadCultureAsync(culture)`** — awaits every provider's catalogs for the culture (and its parent chain),
  the asynchronous ones included. Await it before the UI renders the culture and the subsequent synchronous
  lookups resolve an already-loaded snapshot, with **no flash**. It loads catalogs only; setting the active
  culture is the caller's concern.
- **`PreloadAllAsync()`** — the awaited "load everything" for an asynchronous context (server startup):
  every known culture from every provider, both arms awaited.
- **`CatalogsChanged`** — raised after any commit that changed the snapshot. A synchronous miss on an
  asynchronous culture returns the in-code default now and queues a coalesced background load; when it lands,
  `CatalogsChanged` fires and the UI layer re-renders (stale-while-revalidate). An inline synchronous load
  resolves directly and needs no event.

## Eager vs on-demand culture loading

By default the directory layer reads **every** culture's files up front, into one merged snapshot — right
for a server that handles many cultures at once and cannot predict which a given request needs. Set
`CultureLoading.OnDemand` and the store instead reads a culture's files only the first time that culture is
requested, so a single-user client (CLI, desktop, Blazor) keeps just the active language in memory:

```csharp
Localizer.Initialize(new LocalizerOptions { CultureLoading = CultureLoading.OnDemand });
```

A **language switch is live — no restart.** The first lookup in a not-yet-loaded culture reads that
culture's (small) files, its parent chain (`de-AT` → `de`) included, and swaps the snapshot in atomically;
every lookup after, and every lookup once a culture is loaded, is the same lock-free read as eager loading.
Switching back to an already-loaded culture is free, and a culture with no file falls to the in-code default
as always.

> On-demand applies to the **file** layer. Satellite assemblies are already per-culture — they load the
> first time a culture is used regardless of this setting — and the in-code default needs no file at all.

## ICU MessageFormat and plurals

Defaults and translations are written in **ICU MessageFormat**, the same grammar `.po`/`.arb` translators
already use, so a string carries its own grammar rather than relying on string concatenation that breaks
in other languages. The full surface is supported: simple arguments, typed formatting
(`{name, number, style}` for numbers, currency, and compact — see the next section — plus
`{name, date|time, style}`), `plural` / `selectordinal` (with `offset`, `=N` exact-match selectors, and
`#` for the formatted count), `select` for arbitrary categories, and arbitrary nesting of all of these.
Crucially, **plural categories resolve against the target culture** from embedded Unicode
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

## Number, currency & compact formatting

The `number` placeholder type takes either a **named style** — `integer`, `currency`, `percent` — or an
ICU **`::`-skeleton**: a space-separated list of stems that compose (a currency code, a width, a fraction
rule, a compact notation, and so on). Either way the output is **CLDR-48-faithful** — locale grouping, the
decimal separator, the amount↔symbol spacing, and the negative-number pattern all come from the same
pinned Unicode CLDR data that drives plural selection above. The style slot is **ICU-only**; a `.NET`
format string is never accepted there, and an unrecognized stem throws `MessageFormatException` at parse
time rather than falling back to something plausible.

The accepted skeleton subset:

| Stem | Effect |
|------|--------|
| `currency/<ISO>` | Currency, explicit ISO code — **does not follow the UI language** |
| `unit-width-short` / `-narrow` / `-iso-code` / `-full-name` | Currency width: symbol / narrow / ISO code / full plural name |
| `.00` / `.##` / `.0#` | Fixed / optional / mixed fraction digits |
| `percent` | Percent |
| `integer` (`precision-integer`) | Integer, no fraction |
| `group-off` / `group-auto` | Grouping off / default |
| `compact-short` (`K`) / `compact-long` (`KK`) | Compact notation (stems combine, e.g. `::compact-short currency/USD`) |

The ISO code pins *what* currency is shown, while CLDR alone decides *how* — grouping, decimal comma vs.
point, and where the symbol sits. The clearest way to see the same value under two cultures is
`ToLocalizedString`, which formats in the culture you pass:

```csharp
using ArchPillar.Extensions.Localization.MessageFormat;

1234.56m.ToLocalizedString("::currency/USD", CultureInfo.GetCultureInfo("en-US")); // "$1,234.56"
1234.56m.ToLocalizedString("::currency/USD", CultureInfo.GetCultureInfo("de-DE")); // "1.234,56 $" (U+00A0)
```

The same style works inside a message, where the amount formats in the culture the message is *rendered in*:

```csharp
localizer.Translate("cart.total", "Total: {amount, number, ::currency/USD}", ("amount", 1234.56m));
// under en → "Total: $1,234.56"
```

> Inside a message the number follows the culture the message is *rendered in* — the target culture once
> the string is **translated** for that culture, and the source culture while it still falls back to the
> in-code default (an untranslated English default is rendered by English rules, and its numbers with it).
> So a message localises its numbers once translated; to format a value in a specific culture regardless of
> translation state, use `ToLocalizedString`. The amount↔symbol space in `de-DE` is a non-breaking space
> (U+00A0), per CLDR; a bare `{amount, number, currency}` would follow the culture instead of pinning USD —
> see [recommendations.md](recommendations.md).

**Currency width** picks how the currency itself is written, independent of the amount or the culture.
All four widths for the same `en-US` value:

```csharp
CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
localizer.Translate("cart.total", "{amount, number, ::currency/USD unit-width-short}", ("amount", 1234.56m));
// → "$1,234.56"
localizer.Translate("cart.total", "{amount, number, ::currency/USD unit-width-narrow}", ("amount", 1234.56m));
// → "$1,234.56"
localizer.Translate("cart.total", "{amount, number, ::currency/USD unit-width-iso-code}", ("amount", 1234.56m));
// → "USD 1,234.56"   (the space is a non-breaking space, U+00A0)
localizer.Translate("cart.total", "{amount, number, ::currency/USD unit-width-full-name}", ("amount", 1234.56m));
// → "1,234.56 US dollars"
```

> The `unit-width-iso-code` join (`USD 1,234.56`) uses the same CLDR currency-spacing non-breaking space
> (U+00A0) as the plain currency example above. `unit-width-short` and `unit-width-narrow` have no join
> space at all (`$` prefixes the amount directly), and the `unit-width-full-name` join is an ordinary space.

**Compact notation** abbreviates large magnitudes instead of grouping every digit; the stem composes with
a currency the same way a width does:

```csharp
localizer.Translate("cart.total", "{amount, number, ::compact-short currency/USD}", ("amount", 1234m));
// → "$1.2K"
```

### Formatting a value outside a message

Not every number lives inside a translated sentence — a table cell or a chart axis just needs the value
itself, in the same CLDR formatting, with no template around it. `ToLocalizedString` is the same engine
and the same style syntax as an extension method on the value:

```csharp
using ArchPillar.Extensions.Localization.MessageFormat;

// The same engine and the same style syntax, for a value shown on its own.
CultureInfo de = CultureInfo.GetCultureInfo("de");
1234.56m.ToLocalizedString("::currency/USD", de);   // "1.234,56 $"  (the space is U+00A0)
0.5.ToLocalizedString("::percent");                 // "50%" in en, "50 %" in de (U+00A0)
```

Overloads exist on `decimal`, `double`, `int`, `long`, and `IFormattable`. The culture parameter defaults
to **`CultureInfo.CurrentUICulture`** — the same culture the localizer renders against — never
`CurrentCulture`, which is what makes the two surfaces consistent: `v.ToLocalizedString(s, c)` always
renders identically to `{v, number, s}` rendered in `c`. The extensions live in the
`ArchPillar.Extensions.Localization.MessageFormat` namespace, so add the `using` line above to call
them — everything else in this library resolves without it.

## Container formats

Catalogs round-trip through three standard, translator-tooling-friendly formats, all bundled into the
runtime (no separate packages, no plugin to register): **XLIFF 2.1** (the default — the XML interchange
standard most TMS tools speak, with source and translation as distinct first-class fields), **ARB** (a
JSON dialect with rich metadata), and **Portable Object** (gettext `.po`). You author in whichever your
translation pipeline prefers and the runtime loads all three side by side; when one catalog exists in
more than one format the higher-fidelity file wins (`xliff` > `arb` > `po`, a fixed tie-breaker) and the
loser is never opened. Each provider (`ArbTranslationFormat`, `XliffTranslationFormat`,
`PoTranslationFormat`) is public and **stream-based**, so a catalog can come from anywhere — a file, an
embedded resource, an HTTP response, a database column — and you can build a custom `ICatalogProvider`
on top of one.

## Compile-time extraction and the typed key registry

A Roslyn source generator extracts every translatable call site into a source-language template on a
real build (never at design time, so editing never churns files), and emits a strongly-typed key
registry so call sites and the analyzer share rename-safe keys. "Translatable call site" is not a name
match — it is driven by the `[Translatable]` / `[TranslationDefault]` parameter attributes on the API,
which is why `Translate(...)`, an `L(...)` marker, and even your own wrappers — methods or indexers — are
all recognised the same way. The shared analyzer then surfaces, in the editor as you type, what would
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

Some teams prefer a **string id** to text-as-key. For them an **optional twin attribute** carries just the
source-language default, while the stable id stays where the framework already reads it — in the system
attribute's value. So the system attribute holds the key and the twin holds the default text:

```csharp
public sealed class RegisterModel
{
    [Display(Name = "Email address")]              // text-as-key: key and default are both "Email address"
    public string Email { get; set; } = "";

    [Display(Name = "register.password.label")]    // the string id is the key the framework looks up
    [LocalizedDisplayName("Password")]             // the twin supplies the source default for that id
    public string Password { get; set; } = "";
}
```

Because the framework looks up the system attribute's value either way, that value is the catalog key
directly — there is no remapping. There is one twin per display concept — `[LocalizedDisplayName]` (for
`[DisplayName]` and `[Display(Name)]`) and `[LocalizedDescription]` (for `[Description]` and
`[Display(Description)]`) — plus one generic twin for the open-ended validation case,
`[LocalizedMessage<TValidation>]`, where the **validator's `ErrorMessage` is the key** and the type
argument names which validator the default belongs to (so a property with several validators stays
unambiguous):

```csharp
[Required(ErrorMessage = "register.email.required")]
[StringLength(100, ErrorMessage = "register.email.tooLong")]
[LocalizedMessage<RequiredAttribute>("An email address is required.")]
[LocalizedMessage<StringLengthAttribute>("That email is too long.")]
public string Email { get; set; } = "";
```

**Enums** read their own annotation at runtime: `value.GetLocalizedDisplayName()` reads the member's
`[Display(Name)]` value as the key (and a `[LocalizedDisplayName]` twin as the source default) and resolves
it through the localizer under the enum's category — the localized replacement for the usual hand-rolled
`GetDisplayName()`.

```csharp
public enum AccountStatus
{
    [Display(Name = "Active")] Active,                                  // text-as-key
    [Display(Name = "account.suspended")] [LocalizedDisplayName("Suspended")] Suspended,   // string id + default
}

string label = AccountStatus.Suspended.GetLocalizedDisplayName();   // resolves account.suspended
```

**ASP.NET MVC / Razor Pages** route their DataAnnotations through the localizer with one call on the MVC
builder (in the `…Localization.AspNetCore` package): display names *and* validation messages resolve under
the model type's category, by the system attribute's value (text or string id) as the key, falling back to
the twin's default when no translation is loaded.

```csharp
builder.Services.AddControllersWithViews().AddArchPillarDataAnnotationsLocalization();
```

> Reading a member's attributes is reflection at runtime — inherent to attributes, which the rest of the
> library avoids. For Minimal APIs and Blazor's new validation, the .NET 11 `IValidationLocalizer` /
> `ErrorMessageKeyProvider` seam is a separate follow-up; the MVC integration above needs none of it.

## Dependency injection

`AddArchPillarLocalization` (in the `…Localization.DependencyInjection` package) configures a single
`LocalizationContext` from `LocalizerOptions` and registers the native views over it — `ILocalizer`,
`ILocalizer<T>`, and `ILocalizerFactory`:

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

> `.resx` keys, a bare validator `ErrorMessage`, and view-localization calls are **not** extracted (they
> have no in-code default to harvest); the adapter still serves them at runtime. Display **annotations**
> (`[DisplayName]` / `[Display]` / `[Description]`, and a validator `ErrorMessage` paired with a
> `[LocalizedMessage<T>]` twin) are extracted separately — see
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

There is no dedicated pseudo-localization source — a catalog is the only override mechanism, so a
pseudo-locale is just another catalog. Author a `Translations/<AssemblyName>.qps-ploc.xliff` (or `.arb` /
`.po`) file with every string accented and length-expanded (`[!!! Ḩéłłö !!!]`), ICU placeholders left
intact, and switch to it like any other culture. It is a fast, language-free QA pass: any string that
comes out in plain Latin was never extracted, and any layout that clips or wraps badly will break under
genuinely longer languages too.

```csharp
CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("qps-ploc");
localizer.Translate("home.title", "Home");   // resolves the qps-ploc catalog like any other override
```

## Hot reload

Turn on `EnableHotReload` (debounced by `HotReloadDebounce` so a flurry of saves coalesces into one
reload) and the store — the ambient one, or a `LocalizationContext` — watches its directory and reloads
on change. A lookup always resolves against the **latest** snapshot, swapped atomically, so concurrent
`Translate` calls never tear or block — an in-flight render finishes against the old snapshot and the
next lookup sees the new one. Edit a translation file and the running app reflects it without a restart.

```csharp
using var context = new LocalizationContext(new LocalizerOptions
{
    TranslationsDirectory = "Translations",
    EnableHotReload = true
});
string s = context.Default.Translate("home.title", "Home");   // reads the live, hot-reloaded snapshot
```

## Isolated localizers

When you want a localizer that shares nothing with the ambient store, construct a
[`LocalizationContext`](#the-localization-context) — its own configuration, directory, watcher, and the
`For<T>()` / `Configure` surface. There is no lower-level "bare engine" door: the resolution engine
(`DefaultLocalizer`) is `internal`, built only by a `LocalizationContext` (or the ambient `Localizer`)
over its own store — a consumer never constructs one directly.

For a fixed set of catalogs with no file system — Blazor WebAssembly fetching and parsing catalogs over
HTTP, or a test that wants no disk I/O — hand them to an `InMemoryCatalogProvider` and layer it into a
context the same way any other provider is added:

```csharp
var options = new LocalizerOptions { Providers = [_ => new InMemoryCatalogProvider(catalogs)] };
using var context = new LocalizationContext(options);
string s = context.Default.Translate("home.title", "Home");
```
