# AI Agent Instructions — ArchPillar Extensions

## Project Overview

A monorepo of standalone .NET libraries published under the `ArchPillar.Extensions` namespace. Currently contains:

- **Mapper** (`src/Mapper/`) — object-to-object DTO mapping and LINQ/EF Core expression projection. Read `docs/mapper/internals/SPEC.md` for full design philosophy and API surface.
- **Localization** (`src/Localization/`) — UI-string translation: extract translatable call sites, hand them to translators in standard formats (ARB/XLIFF/PO), and load translations at runtime as pluggable overrides; the in-code default stays the source of truth and terminal fallback. A multi-package family with optional adapters (DI, ASP.NET Core, IStringLocalizer interop, Blazor WebAssembly), Roslyn analyzers/code-fixes, and a `Tooling` dotnet tool. Read `docs/localization/internals/SPEC.md`.
- **Primitives** (`src/Primitives/`) — foundational result types (`OperationResult`, `OperationResult<TValue>`, `OperationStatus`, `OperationError`, `OperationException`) for returning HTTP-aligned outcomes; the `Primitives.EntityFrameworkCore` companion makes `Id<T>` typed identifiers a first-class SQL type. Read `docs/primitives/internals/SPEC.md`.
- **Pipelines** (`src/Pipelines/`) — a lightweight, allocation-free async middleware pipeline (`Pipeline<T>`, pre-composed nested lambdas) with Microsoft.Extensions.DependencyInjection integration. Read `docs/pipelines/internals/SPEC.md`.
- **Commands** (`src/Commands/`) — a lightweight in-process command dispatcher built on Pipelines; cross-cutting concerns (validation, transactions, logging) plug in as middlewares, handlers register through DI, AOT/trim-safe. Read `docs/commands/internals/SPEC.md`.

When writing or reviewing **documentation, sample projects, or LLM agent skills**, follow the authoring standards in `docs/authoring/` — `documentation-guide.md` (user-vs-developer audience split, canonical skeletons, house style), `samples-guide.md` (naming, structure, the per-sample README), and `skills-guide.md` (one skill per library, generate from the SPEC, the compile/run oracle against the published package). Each guide ends with a review checklist; apply it before considering a docs/samples/skills change done. For the general skill-authoring craft (frontmatter, CSO, progressive disclosure, RED-GREEN-REFACTOR), `skills-guide.md` defers to the `superpowers:writing-skills` skill — use it, don't duplicate it.

## Design Principles

These govern how every library in the monorepo is designed and evolved. Give each type one job and one owner, and remove anything that exists for a reason that no longer holds — a design that "feels off" usually hides a second door, a leaked internal, or speculative machinery. Find it and cut it.

- **KISS** — write the simplest thing that meets the requirement; no cleverness the problem didn't ask for.
- **YAGNI** — don't carry machinery for needs that aren't real. Delete a feature once its original reason no longer holds, and prefer a concrete requirement on the caller over a generic extension point nobody uses.
- **Subtraction is progress** — favour the change that removes more than it adds and reads clearer afterward.
- **One job, one owner** — each type does one thing, and each fact or decision has exactly one owner. If you can't state a type's job in a sentence, it is two types.
- **One door per concern** — exactly one path to accomplish a given thing. Two parallel mechanisms for the same job is a smell; remove the redundant one.
- **One composition root, explicit wiring** — wiring happens in one place and dependencies are passed, not discovered. No ambient DI, global state, static registries, configuration attributes, or convention-based guessing.
- **Encapsulate the shape, expose the intent** — hand callers methods (verbs), never the internal data structure, so the representation stays free to change.
- **Fix the root, never patch** — a dependency that is awkward to thread means the design is wrong; change the design instead of smuggling it through.
- **Question the spec, not just the code** — when something is overcomplicated, be willing to delete or redesign the feature, not merely refactor its implementation.
- **Build and tests are the oracle** — keep both green with zero warnings after every step; trust them over stale IDE diagnostics; move in small, reversible steps.
- **Respect the platform's real limits** — know the target frameworks, and don't force a modern idiom where a target cannot honor it.
- **Draw it when it feels off** — a quick relationship diagram surfaces seams that code review misses.

## Build & Test Commands

```bash
dotnet build                                          # build all projects
dotnet test tests/Mapper.Tests                        # run ALL tests (including PostgreSQL)
dotnet run --project benchmarks/Mapper.Benchmarks -c Release  # run benchmarks
```

**CRITICAL: Zero warnings policy.** Warnings are treated as errors — in ALL builds, not just Release. Every `dotnet build` must produce **zero warnings and zero errors** before moving on. After writing or modifying any code, run `dotnet build` and fix every warning before proceeding. Common violations to watch for:
- `IDE0007`: Use `var` instead of explicit type — applies to ALL built-in types (`int`, `string`, etc.) and apparent types
- `IDE0008`: Use explicit type instead of `var` — when the type is NOT apparent from the right-hand side (e.g. method return types like `GetProperty()` returning `PropertyInfo?`)
- `IDE0032`: Use auto property instead of backing field
- Other analyzer rules in `.editorconfig` — read and follow them strictly

### PostgreSQL Tests

The test suite includes PostgreSQL integration tests that verify SQL translation against a real database. **Always run the full test suite** (`dotnet test tests/Mapper.Tests`) including PostgreSQL tests — do not skip or filter them out.

PostgreSQL test infrastructure (`PostgresTestDatabase`):
- **Podman available**: Uses Testcontainers to spin up an ephemeral PostgreSQL container (Testcontainers talks to the Podman socket; this project uses Podman exclusively, not Docker)
- **Cloud environment** (`CLAUDE_CLOUD=true`): Falls back to the host-local PostgreSQL instance (`Host=localhost;Port=5432;Username=app;Password=postgres`) when Podman is unavailable. Start it with `pg_ctlcluster 16 main start` if needed.

Each test class gets an isolated database (created/dropped automatically).

## Architecture

- `src/Mapper/` — core library (public API + `Internal/` infrastructure)
- `tests/Mapper.Tests/` — xUnit tests with EF Core in-memory provider
- `benchmarks/Mapper.Benchmarks/` — BenchmarkDotNet performance tests
- `MapperContext` is the central abstraction (modeled after EF Core's `DbContext`)
- Expression trees power both in-memory mapping and LINQ projection from a single definition

## Build Infrastructure

- `Directory.Build.props` — shared project properties (framework, lang version, nullable, authors)
- `Directory.Build.targets` — Roslyn analyzers (Roslynator, SonarAnalyzer.CSharp) applied to all projects
- `.editorconfig` — enforced code style, naming conventions, and analyzer severity overrides
- `NuGet.Config` — package source configuration (nuget.org only)

## Code Style (enforced by .editorconfig)

### Language & Framework
- .NET 8/9/10, C# 14 (`LangVersion 14.0`)
- `<Nullable>enable</Nullable>` — strict nullable reference types everywhere
- `<ImplicitUsings>enable</ImplicitUsings>`
- File-scoped namespaces: `namespace Foo;` (warning-level enforcement)
- Line endings: LF (Unix-style) for all files except Razor/HTML (CRLF)
- UTF-8 encoding, final newline required, no trailing whitespace

### Naming (error-level enforcement)
- **All fields** (instance and static): `_camelCase` (underscore prefix)
- **Constants**: `PascalCase` (no prefix)
- **Parameters / locals**: `camelCase`
- **Local functions**: `PascalCase`
- **Types / methods / properties / all other members**: `PascalCase`
- **No abbreviations** — use full words (`sourceParam`, not `srcP`)

### var Usage (IDE0007 / IDE0008 — error severity)
- **Default to `var` everywhere** — the analyzers enforce `var` at error level for apparent types AND built-in types (`int`, `string`, `bool`, `decimal`, etc.)
- Use `var` when the type is apparent from the right-hand side: `var x = new Foo()`, `var y = GetFoo()`, `var mid = (lo + hi) / 2`
- Use `var` for built-in types: `var count = 0;` not `int count = 0;`, `var name = "foo";` not `string name = "foo";`
- Use explicit types when the type is NOT apparent and NOT a built-in (e.g. `IReadOnlyList<int> items = GetItems();` where the method returns `List<int>`)
- **Method chains change the apparent type rule**: `var x = new Foo()` is apparent, but `var x = new Foo().Bar()` is NOT — the chained call obscures the final type. Use an explicit type: `SomeType x = new Foo().Bar();`
- **Common traps**: `expr.Compile()` → use `Func<X, Y> func = ...`; `Assert.Throws<T>()` → use `T ex = ...`; `type.GetProperty()` → use `PropertyInfo? prop = ...`
- **In practice**: if in doubt, use `var`. The analyzer will catch the rare cases where an explicit type is preferred.

### Expression-Bodied Members
- **Use** for: accessors, indexers, properties (warning)
- **Do not use** for: constructors (error), methods, operators, local functions, lambdas (suggestion)

### Formatting
- Allman brace style (braces on new lines for all constructs)
- Always use braces for control flow, even single-line bodies (warning)
- Align related assignments vertically when it improves readability
- Sort `using` directives: system first, no blank line groups
- No `this.` qualifier (warning)
- Use language keywords over BCL types (`int` not `Int32`) (warning)
- Use target-typed `new()` when type is apparent (warning)
- Use collection initializers (error), object initializers (error)
- Use null propagation `?.` (error), coalesce `??` (suggestion)
- Prefer pattern matching over `as`+null check and `is`+cast (warning)
- Prefer switch expressions (warning)
- Mark fields `readonly` when possible (error)
- Prefer `nameof` over string literals (warning)
- Primary constructors for simple types
- Records for immutable data types

### Access & Sealing
- Always specify accessibility modifiers (warning)
- Public API types: `public`
- Internal infrastructure: `internal`
- Seal classes that are not designed for inheritance: `sealed class`

### Documentation
- XML doc comments (`///`) on all public types and members
- No XML docs on internal or private members
- No inline `//` comments unless the logic is non-obvious

### Expression Trees
- Expressions must be translatable by EF Core — no `throw`, no delegate invocations, no unsupported method calls
- Use `ExpressionVisitor` subclasses for transformations
- Lazy compilation via `Lazy<Func<>>` for deferred work

### Error Philosophy
- Every destination property must be explicitly mapped, optional, or ignored — unmapped properties cause a build-time exception
- Fail fast with clear messages at build time, not at query time
- No silent defaults or convention-based guessing

## Analyzers

Three analyzer packages are active via `Directory.Build.targets`:
- **Roslynator.Analyzers** + **Roslynator.Formatting.Analyzers** — code style and formatting
- **SonarAnalyzer.CSharp** — code quality and security

Key severity overrides are configured in `.editorconfig`. When an analyzer fires, fix the issue rather than suppressing it, unless there's a clear false positive (check existing suppressions in `.editorconfig` for precedent).

## Test Conventions

- Framework: xUnit
- Naming: `MethodName_Scenario_ExpectedBehavior`
- Shared mapper setup lives in `TestMappers.cs` (a `MapperContext` subclass)
- Model classes live in `TestModels.cs`
- Use Arrange-Act-Assert pattern
- Assertions: prefer `Assert.Equal`, `Assert.Null`, `Assert.NotNull`, `Assert.Throws<T>`
- Test EF Core translation with `Microsoft.EntityFrameworkCore.InMemory`

## What NOT To Do

- Do not add convention-based auto-mapping or property name matching
- Do not introduce global state or static registries
- Do not use reflection at runtime (only during initial expression compilation)
- Do not add attributes for mapping configuration
- Do not emit `throw` expressions inside expression trees (EF Core cannot translate them)
- Do not add external package dependencies — the library relies only on BCL types (`IQueryable`, `System.Linq.Expressions`)
- Do not use `#pragma warning disable` — suppress warnings via `.editorconfig` scoped sections instead
- **NEVER add, modify, or remove analyzer suppressions or severity overrides in `.editorconfig` without explicit user approval.** Always stop and ask, even if the suppression seems obvious or is needed to make the build pass. This applies to all diagnostics: Roslyn, Roslynator, SonarAnalyzer, and IDE rules.
- Do not use `this.` qualifier on members
- Do not create README.md or other documentation files unless explicitly asked
- Do not schedule recurring check-ins or self-wakeups for pull-request work (no `send_later`, cron, or `/loop` polling of CI, reviews, or merge state). Push the work, report once, and stop; act on pull-request events when they arrive, and let the user ask for a status check when they want one.
