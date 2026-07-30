# 02 — Extraction, Template Emission & Reconciliation

> Assemblies: the compile-time `IIncrementalGenerator` that emits the `Localized<T>` constructor and DI-registration sources ships inside `ArchPillar.Extensions.Localization.Analyzers` (consolidated with detection and the analyzer — see spec 01), and `ArchPillar.Extensions.Localization.Tooling` is the `dotnet` tool that extracts the **template** (from IL) and adds/syncs/converts **target** files on demand (it carries the extract/merge core). References the detection core (spec 01), the `Catalog` model and provider interface (spec 03), and `MessageFormat` (spec 04).

## Purpose

Two separable jobs, deliberately assigned to different hosts so that **target languages are never a build or developer decision** (Decision D-12):

1. **Template extraction (tool, build-time or on demand).** Recover the call sites from a **built assembly's IL** (Decision D-K) into the language-neutral **template**: a `Catalog` with the source language, every key, source text, and metadata, no target translations. The build runs this for you (spec 06). The generator's compile-time output is in-assembly source — the `Localized<T>` constructor and DI-registration sources — never a file or template.
2. **Target operations (tool, on demand).** From the template, create a new target-language file (`add`), reconcile existing target files against the current template (`sync`), or re-serialize into another format (`convert`). None of this is a build input; it happens when localization is wanted, run by whoever owns it.

Reconciliation — adding new keys, **deleting** keys that no longer exist in code (Decision D-11), and flagging keys whose source default changed while keeping the translation — is the gettext `xgettext` (extract → template) + `msgmerge` (sync) pair, rebuilt to our model. Both extraction (from IL) and merge live in the tool; the generator emits no template — only the `Localized<T>` in-assembly source.

The merge is the hardest engineering in the library. The parser and the runtime are bounded; reconciliation is where the real design lives. Budget accordingly.

## In scope

- Building the template from a built assembly's IL (tool); the `Localized<T>` constructor and DI-registration sources from detection (generator).
- Fingerprinting for staleness detection.
- The reconcile/merge algorithm and its state transitions, including delete-on-removal (tool `sync`).
- New target-file creation (tool `add`) and format conversion (tool `convert`).
- The tool's IL extraction and its command-line surface.

## Out of scope

- File format syntax (delegated to `Formats.*`, spec 03).
- Message rendering (runtime, spec 05).
- Any notion of a declared target-language set — there is none.

## Template extraction (from compiled IL)

The source-language template is produced by the **tool**, not the generator — a generator cannot do file I/O, and reading the built assembly's **IL** (Decision D-K) lets extraction also cover strings the syntax-level detector never sees (Razor/Blazor/MVC generated code). `TemplateBuilder` runs the IL extractor (`AssemblyStringExtractor`, Mono.Cecil) over each in-scope assembly and recognises a translatable call by the **same `[Translatable]`/`[TranslationDefault]` parameter attributes** the source-level detector reads — so the native `Translate`, the `L(...)` marker, and user wrappers (methods or indexers) are all found from metadata alone, without loading code. The category is taken from a `[TranslationScope]`-marked argument found by walking the receiver's own type and its base types/interfaces (so `ILocalizer<T>` and `Localized<TSelf>` resolve identically to the runtime). An assembly with no translatable call sites yields **no template** — empty files are never written.

On a real build the package's MSBuild target runs the tool's `extract` for the project automatically (spec 06); the tool also runs over a `--solution`/`--project`/`--input` scope (defaulting to the current directory) on demand, for CI or a manual pass. The write is atomic — temp file then move (spec 03) — and skips an unchanged file, so a no-op build touches nothing.

### The annotation pass

A second pass, `AssemblyStringExtractor.ExtractAnnotations`, recovers display text carried by attributes rather than call sites — the strings ASP.NET model metadata and reflection consumers render. It walks the assembly's own types, properties, and fields (enum members), reading `[DisplayName]`, `[Display(Name)]`/`[Display(Description)]`, and `[Description]`; the category is the **declaring type's full name** (Cecil's `/` nested-type separator normalized to reflection's `+` so a catalog key matches the runtime lookup). The **system attribute's value is the key** — its own text in the text-as-key default, or a string id when the author prefers one — because that value is what the framework looks the string up by. An optional `[Localized…]` twin (`[LocalizedDisplayName]`, `[LocalizedDescription]`) supplies the source default for that key instead of reusing the key as its own default; with no twin, key and default are the same value. The generic `[LocalizedMessage<TValidation>]` is the validation form: its key is the `ErrorMessage` of the validator named by the type argument (skipped when that validator sets none), so a member can carry one twin per validator. Unlike the call-site pass, it does not early-out on a localizer reference — an annotated model need not touch `ILocalizer`. `TemplateBuilder.Build` folds the pass in by default (`includeAnnotations`), call sites taking precedence on a shared `(category, key)`; the consumer opts out with `ArchPillarLocalizationExtractAnnotations=false`, which passes `--no-annotations` (spec 06). The runtime counterparts that read the same attributes by reflection are the enum helper `GetLocalizedDisplayName()` (spec 05) and the ASP.NET DataAnnotations integration (the `…AspNetCore` package).

### Source references (from the PDB)

An entry records the **files** its string is used in — never the lines (Decision D-N). Symbols are read alongside
the IL (`throwIfNoSymbol: false`, so a missing PDB is not an error); for each recognised call the governing
sequence point is the nearest preceding one by IL offset, since a call instruction never carries one of its own.
A **hidden** point (`0xFEEFEE`) yields no reference: that is what a generated view emits around the code between
its markup expressions, and reading through it would attribute the call to an unrelated line.

Coverage follows from that rule. A **Blazor** `.razor` component resolves cleanly — its generator maps whole
statements back to the component, so the reference is `Components/Pages/Home.razor`. An **MVC/Razor Pages
`.cshtml`** markup expression has no usable sequence point and therefore gets no reference. Annotations get none
either: a field or an attribute application has no debug location at all.

Paths are recorded relative to the project root with `/` separators, sorted, deduplicated, and unioned across
every call site of a `(category, key)` — so the catalog is byte-identical on every machine. A path that will not
relativize (a deterministic `/pathmap` build, a loose `--assembly`) is dropped rather than written absolute, and
the reconciler preserves whatever the catalog already had.

### Translation comments (from source)

A translation comment cannot come from the assembly: the compiler strips `//` and `/* */` from IL and the PDB, and a PDB has no source location for a field or an attribute at all (only method bodies get sequence points). So comments are recovered by a separate **syntax-only** source scan (`SourceCommentScanner`, using `Microsoft.CodeAnalysis.CSharp` — no compilation, no semantic model, no metadata references), run once per project root and shared across the assemblies it built. The root is the assembly's own project directory (`CatalogDirectoryResolver.ProjectRootOf`); a loose `--assembly`/`--input` path with no project tree is simply scanned for nothing.

The convention is that a comment is written **inside the argument list** — of the call, the indexer, or the annotation:

```csharp
Translate("home.title", "Home" /* shown in the header */);
[Display(Name = "Active" /* the running state */)]
[LocalizedDisplayName("Log in" /* on the button */)]
```

The scanner associates each in-paren comment with the nearest string literal in that list and indexes it by that literal. `TemplateBuilder` then joins the comment to an entry by **identity** — the entry's key or its source default (`CommentIndex.Lookup`) — so a note written by the key (a string id) or by the human default reaches the entry without any source location, in the call or in either paired annotation. Comments on multiple sites of one key are combined; call sites need no per-site precision because the catalog holds one entry per `(category, key)`. Because the scan parses rather than pattern-matches, a `//` or `/* */` inside a string literal (a URL, a percent sign) is correctly *not* a comment.

Because comments live only in source, an extract that cannot see it — a `/pathmap` CI build, a stripped or absent PDB (though the scan uses source, not the PDB, the same source-less conditions apply), a loose `--assembly` with no project — yields no comment, and the reconciler **preserves** any comment already in the git-tracked catalog rather than wiping it.

**Deferred / not recovered:** leading trivia (a comment on the line *above*) — ambiguous on a call, and PDB-invisible on an annotation; a comment on a non-literal argument (a `const`-field reference, an interpolated string; simple `"a" + "b"` literal concatenation *is* folded); an attribute reached only through a `using`-alias; and Razor/Blazor markup comments (the scan is C# `.cs` only). Opinionated reformatters (for example CSharpier) may relocate an in-paren comment; keep it beside its literal.

## Generator: `Localized<T>` bundle sources (compile-time)

The `IIncrementalGenerator` runs inside the compiler and emits in-assembly source — never a file or template; its pipelines key on code only. It emits two sources for `Localized<T>` bundles: the **bundle constructors** (`LocalizedBundleConstructors.g.cs`) — an ambient and an `ILocalizer<TSelf>` constructor for each `partial` bundle that declares none — and the **DI registration** (`LocalizedBundleRegistration.g.cs`), an `internal` `AddArchPillarLocalizedBundles()` extension registering each injectable bundle through that constructor, emitted only when the compilation references the DI abstractions.

## Tool: target operations (on demand)

`ArchPillar.Localization.Tooling` operates on the template and on whatever target files already exist. **The set of target languages is never declared; it is discovered (the files present) or named (the `add` argument).**

- **`add <lang>`** — read the template, build a target `Catalog` for `<lang>` with every entry `NeedsTranslation`, targets empty, and the correct per-language header written by the provider (Portable Object `Plural-Forms` from CLDR via `PluralRules.GettextOrder`, XLIFF `trgLang` + `state="initial"`, ARB `@@locale`); write it to `OutputPath`. Refuses (or `--force`) if the file exists. This is the new-language path; it is the reconcile core starting from an empty target.
- **`sync`** — for each existing target file in `OutputPath`, run the reconcile algorithm below against the current template. This is `msgmerge`, run deliberately, not on every build.
- **`convert --to <format>` `[--lang <lang>|--template]`** — read the named file with its current provider and write it with another (spec 03). A thin pass over the `Catalog` model; see Conversion below.
- **`--check`** — run `sync`/template comparison in memory and exit nonzero if anything is out of date, writing nothing (continuous-integration gate).

## The template and the catalog model

The template is the canonical extraction output: a `Catalog` (spec 03) with `Culture = <source language>` and one `CatalogEntry` per distinct `(Category, Key)`. Target files share the same model with `TranslatedMessage` and `State` filled in.

```csharp
public sealed record CatalogEntry
{
    public required string Key { get; init; }
    public required string SourceMessage { get; init; }   // the in-code default (ICU)
    public string? TranslatedMessage { get; init; }       // null/empty in the template
    public string? Comment { get; init; }
    public IReadOnlyList<SourceReference> References { get; init; } = [];
    public IReadOnlyList<string> Placeholders { get; init; } = [];
    public required string SourceFingerprint { get; init; } // see below
    public TranslationState State { get; init; } = TranslationState.NeedsTranslation;
}

public enum TranslationState
{
    NeedsTranslation,  // no translation yet
    NeedsReview,       // translation exists but source drifted, or placeholders changed
    Translated,        // translated, not yet signed off
    Final              // reviewed/approved
}
```

`TranslationState` is the canonical state machine. It maps onto each format (spec 03): XLIFF carries it natively in unit `state`; Portable Object maps `NeedsReview` to the `fuzzy` flag and `NeedsTranslation` to an empty `msgstr`; ARB has no native state, so it is persisted in a metadata attribute.

### Merging duplicate sites

Multiple call sites can share a `(Category, Key)`. Merge them into one entry by unioning `References`. If their `SourceMessage` differs, that is diagnostic `APL0006` (spec 01); the extractor must surface it as a build warning and pick a deterministic winner (first by source location) so output is stable.

## Fingerprinting

`SourceFingerprint = stable_hash(normalize(SourceMessage))`.

- Use a stable, explicit hash (for example SHA-256 over UTF-8, hex-encoded, truncated to 16 bytes). Do **not** use `string.GetHashCode()` — it is not stable across runs or runtimes.
- `normalize` must be deterministic: fixed Unicode normalization form (NFC), no trimming that would change meaning. Define it once and use the identical function in analyzer `APL0009` so live and tooling staleness agree.
- The fingerprint is stored on every per-locale entry as the fingerprint of the source default **that the translation was made against**. Comparing it to the freshly extracted template fingerprint is how drift is detected.

## Reconcile algorithm

The reconcile algorithm is the body of the tool's `sync` (per existing target file) and `add` (starting from an empty target). It is never run by the generator and never driven by a declared language list.

Inputs: the current template `T` (keyed by `(Category, Key)`, read from `OutputPath`); one target catalog `L` — for `sync`, each existing target file discovered in `OutputPath`; for `add`, an empty `L` with the requested culture and a freshly written per-language header. Output: an updated `L'` written back via the provider.

For each target catalog:

1. **Index** `L` by `(Category, Key)`.
2. **For each template entry `t` in `T`:**
   - **New key** (`t` not in `L`): create `l'` with `SourceMessage = t.SourceMessage`, `TranslatedMessage = empty`, `SourceFingerprint = t.SourceFingerprint`, `State = NeedsTranslation`, `References = t.References`, `Comment = t.Comment`, `Placeholders = t.Placeholders`.
   - **Existing key** (`t` matches `l` in `L`):
     - Always refresh non-translation metadata from the template: `References`, `Comment`, `Placeholders`, and `SourceMessage` (the displayed source must reflect current code).
     - **Source unchanged** (`l.SourceFingerprint == t.SourceFingerprint`): keep `TranslatedMessage` and `State` as-is.
     - **Source drifted** (`l.SourceFingerprint != t.SourceFingerprint`): keep `l.TranslatedMessage`, set `State = NeedsReview`, set `SourceFingerprint = t.SourceFingerprint`, and record the previous source so the translator can diff (Portable Object: previous-`msgid` comment `#|`; XLIFF: a `<note>`; ARB: a metadata attribute). **Never blank the translation on drift.**
     - **Placeholder set changed** (regardless of fingerprint): force `State = NeedsReview` and surface a warning. A translation written against a different placeholder set may break rendering.
3. **For each existing entry `l` in `L` with no matching template entry:** **delete it** (Decision D-11). No obsolete marker is written. The value is not lost in practice: target files are git-tracked (recoverable from history) and translation memory re-suggests it by source text if the key returns. *Caution:* a key rename appears as a delete plus a new entry, so the translation does not carry across a rename automatically — translation memory is the recovery path. Drift (step 2) is distinct and never deletes.
4. **Order** the output deterministically in source-reference order (file, then line). Stable ordering keeps version-control diffs clean.
5. **Write** `L'` via the format provider (write-if-changed; atomic).

`add` is exactly this with an empty `L`: every template entry takes the "new key" path, yielding a fully-populated, untranslated target file with the correct header.

## Source-language template handling

The tool writes the source-language template (the `.pot`-equivalent / source `.arb` / source `.xliff`) as the artifact the tool and translators work from and as the fingerprint reference. The runtime does not load it (the in-code default is the source-language truth, Decision D-1), and it is not copied to the application output (spec 06).

## Conversion

`convert` reads a file with the provider for its current format and writes it with the provider for the requested format (spec 03), over the shared `Catalog` model. It applies to the template or to a specific target file. Two effects fall out of the model:

- **Plurals.** Converting to or from Portable Object runs the ICU↔gettext plural conversion (spec 03); among XLIFF and ARB it is lossless (both store ICU verbatim). A `select`/nested-plural message that gettext cannot represent is preserved as ICU text and flagged, never silently dropped.
- **Metadata.** The provider capability flags (spec 03) say what survives. Converting to a less-capable format may drop, e.g., source references or explicit state; `convert` reports each capability loss rather than failing silently.

## Command-line surface (`dotnet` tool)

```
dotnet apl extract   [scope: --solution|--project|--input|--assembly, default cwd]   # template from IL
                         --format <arb|xliff|po, default xliff> --source <bcp47, default en>
                         --catalog-path <PROJECT_SUBPATH, default Translations> | --output <DIR>

dotnet apl add       <CULTURE>                               # create a target file from the template
                         [--catalog-path <PROJECT_SUBPATH> | --output <DIR>] [--force]

dotnet apl sync      [--catalog-path <PROJECT_SUBPATH>]      # reconcile every existing target file
                         [--check] [--report <path>]

dotnet apl convert   (--template | --lang <lang>) --to <po|xliff|arb>
                         --output <FILE>
```

- `--check` (on `sync`) is the continuous-integration gate: run in memory, write nothing, and use diff-style exit codes — `0` up to date, `1` drift detected (re-run `sync` to fix), `2` an error (bad invocation, missing or malformed file). A gate that only needs pass/fail still treats any nonzero as failure; one that wants to retry on drift but stop on error can tell them apart. Diagnostics (drift and errors alike) go to stderr.
- `add` refuses to overwrite an existing target file unless `--force`; this is the only way a *new* language enters, and it is explicit and human-driven.
- `--report` drives dashboards (per-language counts of new, stale, removed, untranslated).
- `extract` produces the same template (byte-identical) for the same built assembly across runs and platforms; this is a test gate.
- The two destination options are distinct and `--output` wins when both are given: `--catalog-path` is a folder **inside each project** (MSBuild's `OutputPath` semantics, and what the build's `ArchPillarLocalizationCatalogPath` resolves to), while `--output` is one directory for every catalog, resolved against the current directory like the `dotnet` CLI's own `--output`.

## Acceptance criteria

- [ ] The generator emits only in-assembly source (the typed key registry and the `Localized<T>` constructor and registration sources); the build's `extract` writes only the template (never a target file), and the project declares no languages.
- [ ] `add de` creates `de` with every entry `NeedsTranslation`, an empty target, and a correct per-language header (Portable Object `Plural-Forms` matches the CLDR-derived order); it does not require a build or a project edit.
- [ ] Running `sync` twice with no template change is a no-op: byte-identical files, `--check` passes.
- [ ] Editing a default message, then `sync`, marks only the affected entry `NeedsReview` in every existing target file, preserves the translation, updates the displayed source and fingerprint, records the previous source, and perturbs no other entry's bytes.
- [ ] Removing a call, then `sync`, **deletes** the entry from every target file; re-adding it yields a fresh `NeedsTranslation` entry (no auto-return; recovery via git/translation memory).
- [ ] A key rename, then `sync`, is a delete plus an add (documented behaviour).
- [ ] Changing only a placeholder name forces `NeedsReview` and a warning on `sync`.
- [ ] `convert --template --to xliff` on a `.pot` template yields an equivalent XLIFF template; converting a plural message to Portable Object and back round-trips equivalently; capability losses are reported.
- [ ] `extract` produces a byte-identical template for the same built assembly across runs and platforms.
- [ ] `--check` exits nonzero when and only when a write would change a file.
- [ ] Fingerprints are identical across operating systems and .NET versions (cross-platform determinism test).
- [ ] A `(Category, Key)` used at five call sites produces one entry with five ordered references.
