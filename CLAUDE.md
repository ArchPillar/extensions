# AI Agent Instructions — ArchPillar Extensions

## Project Overview

A monorepo of standalone .NET libraries published under the `ArchPillar.Extensions` namespace. Currently contains:

- **Mapper** (`src/Mapper/`) — object-to-object DTO mapping and LINQ/EF Core expression projection. Read `docs/mapper/SPEC.md` for full design philosophy and API surface.

## Build & Test Commands

```bash
dotnet build                                          # build all projects
dotnet test tests/Mapper.Tests                        # run ALL tests (including PostgreSQL)
dotnet run --project benchmarks/Mapper.Benchmarks -c Release  # run benchmarks
```

Warnings are treated as errors in Release builds (outside Visual Studio). Always ensure code compiles warning-free. Build warnings must be treated as errors and fixed immediately — do not leave warnings in the codebase.

### PostgreSQL Tests

The test suite includes PostgreSQL integration tests that verify SQL translation against a real database. **Always run the full test suite** (`dotnet test tests/Mapper.Tests`) including PostgreSQL tests — do not skip or filter them out.

PostgreSQL test infrastructure (`PostgresTestDatabase`):
- **Docker available**: Uses Testcontainers to spin up an ephemeral PostgreSQL container
- **Cloud environment** (`CLAUDE_CLOUD=true`): Falls back to the host-local PostgreSQL instance (`Host=localhost;Port=5432;Username=app;Password=postgres`) when Docker is unavailable. Start it with `pg_ctlcluster 16 main start` if needed.

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
- Do not suppress analyzer warnings without discussing with the user first
- Do not use `this.` qualifier on members
- Do not create README.md or other documentation files unless explicitly asked
