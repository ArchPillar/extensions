# 01 — Detection Core & Diagnostic Analyzer

> Assemblies: `ArchPillar.Localization.Abstractions` (attributes), and `ArchPillar.Extensions.Localization.Analyzers` — the single compile-time assembly holding the detection core, the diagnostics, and the generator (spec 02). Code fixes ship separately (`…CodeFixes`, the `Microsoft.CodeAnalysis.Workspaces` boundary). All target `netstandard2.0`. *(Detection, analyzer, and generator were originally three assemblies; they were consolidated into one — see [00-architecture.md](00-architecture.md) "Dependency policy".)*

## Purpose

Define, in one place, what counts as a translatable call and how it is recognized; expose that as a pure Application Programming Interface consumed by the analyzer (live diagnostics), the source generator (compile-time extraction + emission, spec 02), and the optional `dotnet` tool (spec 02); and ship a Roslyn `DiagnosticAnalyzer` that surfaces, in the editor, every condition that would otherwise become a silent extraction or runtime bug.

## In scope

- The marker attributes.
- The call-site recognition rules over the Roslyn semantic model.
- The pure detection Application Programming Interface (`record` outputs, no input/output).
- The diagnostic catalog (identifiers, messages, severities, conditions).

## Out of scope

- Writing any file (that is the extractor, spec 02).
- Parsing the message-format string beyond what the shared `MessageFormat` parser provides (spec 04).
- Code generation of any kind.

## Attributes (in `Abstractions`)

```csharp
// Marks the parameter whose argument is the stable symbolic key.
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class TranslatableAttribute : Attribute { }

// Marks the parameter whose argument is the source-language default (ICU MessageFormat).
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class TranslationDefaultAttribute : Attribute { }

// Optional: marks the parameter carrying disambiguation/translator context.
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class TranslationContextAttribute : Attribute { }

// Optional: marks the parameter carrying a translator comment, OR may be applied
// to the method to supply a constant comment for all its call sites.
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Method, AllowMultiple = false)]
public sealed class TranslationCommentAttribute : Attribute
{
    public TranslationCommentAttribute() { }
    public TranslationCommentAttribute(string comment) => Comment = comment;
    public string? Comment { get; }
}
```

The library's own ergonomic surface (defined in the runtime spec 05) is a small set of methods whose parameters carry these attributes, for example:

```csharp
public static string Translate(
    [Translatable] string key,
    [TranslationDefault] string defaultMessage,
    params (string Name, object? Value)[] arguments);

public static string Translate(
    [Translatable] string key,
    [TranslationDefault] string defaultMessage,
    [TranslationContext] string context,
    params (string Name, object? Value)[] arguments);
```

A consumer may wrap these (e.g., an extension method `this IComponent c` returning `c.Localizer.Translate(...)`) and, as long as the wrapper forwards the same attributed parameters with constant arguments, detection follows the wrapper transparently.

## Detection rules

Given a `Compilation`, walk every `InvocationExpressionSyntax` (and `ObjectCreationExpressionSyntax`, to cover attributed constructor parameters). For each, resolve the target symbol via the semantic model. A call site is a **translation site** when the resolved method/constructor has a parameter carrying `[Translatable]` and the corresponding argument is supplied.

For each translation site, resolve the arguments bound to the attributed parameters, accounting for:

- **Positional and named arguments**, and parameters with default values.
- **`params` collections** (the trailing arguments are runtime values; they are not extracted).
- **Constant folding.** The `[Translatable]` and `[TranslationDefault]` (and `[TranslationContext]`, `[TranslationComment]`) arguments must be **compile-time constants**. Use `SemanticModel.GetConstantValue`. Accept string literals, `const` references, and `nameof(...)`. Accept constant concatenation of constants. A `string`-typed compile-time constant is valid; anything non-constant is **not extractable** and triggers diagnostic `APL0001`.

The detection output is a pure record:

```csharp
public sealed record TranslationSite(
    string Key,
    string DefaultMessage,
    string? Context,
    string? Comment,
    IReadOnlyList<MessagePlaceholder> Placeholders, // parsed from DefaultMessage via MessageFormat
    SourceReference Reference);                       // file path + line/column span

public sealed record SourceReference(string FilePath, int Line, int Column);
```

```csharp
public static class TranslationSiteDetector
{
    // Pure: no input/output. Used by both analyzer and extractor.
    public static IEnumerable<TranslationSiteResult> Detect(
        Compilation compilation,
        CancellationToken cancellationToken);

    // For analyzer per-node use:
    public static TranslationSiteResult? DetectAt(
        SemanticModel model,
        SyntaxNode invocationOrCreation,
        CancellationToken cancellationToken);
}

// Either a successful TranslationSite, or a diagnostic-worthy problem with its location.
public sealed record TranslationSiteResult(
    TranslationSite? Site,
    IReadOnlyList<DetectionProblem> Problems);
```

`DetectionProblem` carries an enum cause and a `Location`, so the analyzer maps each cause to a diagnostic and the extractor maps each cause to a build warning/error. **The cause enumeration is the shared contract** — analyzer and extractor must produce identical diagnostics for the same code.

## Diagnostic catalog

Identifier prefix `APL` (ArchPillar Localization). Default severities chosen so that correctness problems are warnings (visible, non-blocking) and only un-extractable code is an error by default; all are configurable via `.editorconfig`.

| Id | Severity | Condition | Message (paraphrased) |
|---|---|---|---|
| `APL0001` | Error | An argument to `[Translatable]`/`[TranslationDefault]`/`[TranslationContext]`/`[TranslationComment]` is not a compile-time constant. | The argument must be a compile-time constant string so it can be extracted. |
| `APL0002` | Warning | `DefaultMessage` fails to parse as ICU MessageFormat (delegated to the spec-04 parser). | The default message is not valid message-format syntax: {detail}. |
| `APL0003` | Warning | A placeholder appears in `DefaultMessage` but no matching runtime argument name is supplied at the call site (when argument names are statically known via the params-tuple form). | Placeholder '{name}' has no supplied argument. |
| `APL0004` | Info | A runtime argument name is supplied that does not appear in `DefaultMessage`. | Argument '{name}' is not used by the message. |
| `APL0005` | Warning | A `plural`/`selectordinal` construct is missing the required `other` branch. | A plural/selectordinal must include an 'other' branch. |
| `APL0006` | Warning | Two translation sites share the same `Key` (and `Context`) but different `DefaultMessage`. | Duplicate key '{key}' with conflicting default text. |
| `APL0007` | Info | Two translation sites share the same `DefaultMessage` and `Context` but different `Key`. | Identical text under different keys; consider sharing a key. |
| `APL0008` | Warning | `Key` does not match the configured key-naming pattern (only when a pattern is configured). | Key '{key}' does not match the required pattern '{pattern}'. |
| `APL0009` | Hidden/Info | The configured "stale source" sidecar (if the analyzer is given catalog files as `AdditionalText`) shows the on-disk source fingerprint differs from the current default. | The default text has changed since translations were made; re-extract to mark them for review. |
| `APL0010` | Warning | The compilation references `IServiceCollection` and a top-level, constructor-less `Localized<TSelf>` class is not `partial`, so the generator cannot synthesize its constructor or DI registration. | Mark '{type}' partial so its localizer constructor and dependency-injection registration are generated. |

Notes:

- `APL0002`, `APL0005`: the analyzer references the `MessageFormat` parser (spec 04) directly. Because `MessageFormat` is pure and `netstandard2.0`, it is safe to reference from an analyzer assembly. Validation logic is therefore shared with the runtime and extractor — never re-implemented.
- `APL0003`/`APL0004` only fire when argument names are statically recoverable. With the `params (string Name, object? Value)[]` form, the names are literals at the call site and recoverable; with a dictionary built elsewhere, they are not, and these diagnostics simply do not fire (no false positives).
- `APL0006` is the high-value duplicate-key safety net that a stable-symbolic-key model needs. `APL0007` is advisory only.
- `APL0009` requires the analyzer to be fed the catalog files via `AdditionalFiles` in the project. It is optional; the canonical staleness mechanism is the reconciler (spec 02). The analyzer version is a convenience that shows drift live.
- `APL0010` fires only when the DI abstractions are referenced — a project that does not use DI is never nudged. It targets a top-level, non-`partial` `Localized<TSelf>` class that declares no constructor (one that already declares an `ILocalizer<TSelf>` constructor, or is already `partial`, is left alone), and the `…CodeFixes` package supplies the one-click "mark partial" fix.

## Analyzer registration

- Implement one `DiagnosticAnalyzer` registering `RegisterCompilationStartAction`, then `RegisterSyntaxNodeAction` for `InvocationExpression` and `ObjectCreationExpression`, calling `TranslationSiteDetector.DetectAt`.
- Resolve the attribute symbols once per compilation start (cache `INamedTypeSymbol` for each attribute) and bail immediately if `Abstractions` is not referenced, so the analyzer is free on projects that do not use the library.
- A code fix is provided where the change is mechanical and meaning-preserving: `APL0005` ships
  `MissingOtherCodeFixProvider`, which adds an empty `other {}` branch to the flagged default-message
  literal (reusing `MessageSyntax.InsertMissingOtherBranches`, so brace and apostrophe-quoting rules match
  the parser) and leaves the rest of the text untouched for a translator to fill in. `APL0001` has no fix:
  a non-constant argument is genuinely not extractable, and inventing a `const` would change behavior, so
  the analyzer only flags it. The code-fix assembly is `netstandard2.0` and packs alongside the analyzer
  under `analyzers/dotnet/cs`.
- Ship the analyzer and the runtime in the same NuGet package with correct `analyzers/dotnet/cs` packaging so referencing the library activates the diagnostics automatically.

## Acceptance criteria

- [ ] `[Translatable]` on a parameter causes both `Detect` and the analyzer to recognize the call; renaming or removing the attribute removes recognition. No method-name string is hardcoded anywhere.
- [ ] A wrapper method forwarding constants to attributed parameters is detected identically to a direct call.
- [ ] A non-constant key or default produces `APL0001` as an error and is excluded from `Detect` output.
- [ ] `Detect` and the analyzer produce the same `DetectionProblem` causes for the same source (shared-contract test: run both over a fixture project and assert equality of cause+location sets).
- [ ] `APL0002`/`APL0005` reuse the spec-04 parser; a malformed message is flagged identically by analyzer, extractor, and runtime validation.
- [ ] The analyzer is a no-op (zero allocations beyond start-action setup) on a project that does not reference `Abstractions`.
- [ ] `Placeholders` on a `TranslationSite` exactly equals the placeholder set the spec-04 parser extracts from the same `DefaultMessage`.
