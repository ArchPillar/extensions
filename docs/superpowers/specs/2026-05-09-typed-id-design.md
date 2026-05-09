# Typed `Id<T>` Support for ArchPillar.Extensions.Primitives

**Status:** Approved
**Date:** 2026-05-09
**Author:** Tibold Kandrai (with Claude)

## Goal

Add a strongly-typed identifier struct `Id<T>` to `ArchPillar.Extensions.Primitives`, plus a separate EF Core integration package that lets `Id<T>` participate transparently in arbitrary LINQ queries — not just simple property comparisons.

## Motivation

Raw `Guid` IDs offer no compile-time safety: a `User.Id` can be passed where an `Order.Id` is expected and the compiler stays silent. Phantom-typed wrappers (`Id<User>`, `Id<Order>`) eliminate this class of bug while keeping the wire shape and storage shape identical to a plain `Guid`.

Past experience suggests that a property-level `ValueConverter` alone is insufficient — queries that involve parameters, `Contains`, joins on Id columns, or constants of the wrapper type tend to fail in surprising ways. The EF integration must therefore make `Id<T>` a first-class SQL type, not just a convertible property.

## Non-Goals

- Non-Guid backing stores (string, long, ULID). `IId` is intentionally Guid-only.
- Source generators or per-entity strongly-typed Id structs. The single generic struct covers the use case.
- Runtime registries, attribute-driven configuration, or convention-based property-name matching.

## Decisions

| Decision | Choice |
|---|---|
| Type parameter `T` | Phantom marker, no constraint |
| Default value | `default(Id<T>)` and `Id<T>.Empty` both yield `Value = Guid.Empty` |
| Factory | `Id<T>.New()` generates a fresh Guid (v7 on net9+, v4 on net8) |
| Cast direction | Implicit `Id<T> → Guid`; explicit `Guid → Id<T>` |
| Interface | `IId { Guid Value { get; } }` |
| Protocols | `IEquatable<Id<T>>`, `IComparable<Id<T>>`, `ISpanFormattable`, `ISpanParsable<Id<T>>`, `==`/`!=`, `JsonConverter` |
| EF discovery | Auto-convention scan at model build (`UseArchPillarTypedIds`), plus `HasIdConversion<TId>()` per-property escape hatch |
| EF type system | Three layers: `ValueConverter`, `ValueComparer`, `IRelationalTypeMappingSourcePlugin` |
| Project layout | `Id<T>` in `src/Primitives/`; EF integration in new `src/Primitives.EntityFrameworkCore/` |

## Public API

### `ArchPillar.Extensions.Primitives`

```csharp
namespace ArchPillar.Extensions.Identifiers;

public interface IId
{
    Guid Value { get; }
}

[JsonConverter(typeof(IdJsonConverterFactory))]
public readonly struct Id<T>
    : IId,
      IEquatable<Id<T>>,
      IComparable<Id<T>>,
      ISpanFormattable,
      ISpanParsable<Id<T>>
{
    public Guid Value { get; }

    public Id(Guid value);

    public static Id<T> Empty { get; }            // Value == Guid.Empty
    public static Id<T> New();                    // v7 on net9+, v4 on net8

    public static implicit operator Guid(Id<T> id);
    public static explicit operator Id<T>(Guid value);

    // IEquatable / IComparable / formatters / parsers — delegate to Value
    public static bool operator ==(Id<T> left, Id<T> right);
    public static bool operator !=(Id<T> left, Id<T> right);
}
```

- `default(Id<T>)` and `Id<T>.Empty` are observationally identical (both yield `Guid.Empty`).
- All formatting/parsing delegates to `Guid` — `ToString("N"/"D"/"B"/"P"/"X")`, `TryFormat`, `Parse`, `TryParse` behave like `Guid`.
- The JSON converter factory matches any closed `Id<T>` and emits/reads a bare Guid string — wire shape is identical to a plain `Guid`.
- `Id<User>` and `Id<Order>` are distinct CLR types: assignment between them is a compile error.

#### File layout

- `src/Primitives/Identifiers/IId.cs`
- `src/Primitives/Identifiers/Id.cs`
- `src/Primitives/Identifiers/IdJsonConverter.cs`
- `src/Primitives/Identifiers/IdJsonConverterFactory.cs`

#### Guid generation policy

A small private helper picks the version based on target framework:

```csharp
// On net9.0+:
return Guid.CreateVersion7();

// On net8.0:
return Guid.NewGuid();
```

Implemented with `#if NET9_0_OR_GREATER` so a single `Id<T>.cs` compiles cleanly across all target frameworks.

### `ArchPillar.Extensions.Primitives.EntityFrameworkCore`

```csharp
namespace ArchPillar.Extensions.Identifiers.EntityFrameworkCore;

public static class PrimitivesDbContextOptionsExtensions
{
    public static DbContextOptionsBuilder UseArchPillarTypedIds(
        this DbContextOptionsBuilder builder);
}

public static class PropertyBuilderExtensions
{
    public static PropertyBuilder<TId> HasIdConversion<TId>(
        this PropertyBuilder<TId> builder)
        where TId : struct, IId;
}
```

- `UseArchPillarTypedIds()` is the auto-discovery on-switch. Registers an `IDbContextOptionsExtension` that:
  - Adds an `IRelationalTypeMappingSourcePlugin` so any expression of type `Id<T>` (or `Id<T>?`) resolves to a relational Guid type.
  - Adds an `IModelFinalizingConvention` that walks every entity property at model build, identifies properties whose CLR type implements `IId` — checking both the CLR type and `Nullable.GetUnderlyingType` so `Id<T>?` is also covered — and applies the `ValueConverter<Id<T>, Guid>` plus `ValueComparer<Id<T>>`.
- `HasIdConversion<TId>()` is the explicit per-property escape hatch for users who disable conventions, use shadow properties, or want surgical control.

#### File layout

- `src/Primitives.EntityFrameworkCore/PrimitivesDbContextOptionsExtensions.cs`
- `src/Primitives.EntityFrameworkCore/PropertyBuilderExtensions.cs`
- `src/Primitives.EntityFrameworkCore/Internal/ArchPillarPrimitivesOptionsExtension.cs`
- `src/Primitives.EntityFrameworkCore/Internal/IdValueConverter.cs`
- `src/Primitives.EntityFrameworkCore/Internal/IdValueComparer.cs`
- `src/Primitives.EntityFrameworkCore/Internal/IdRelationalTypeMappingSourcePlugin.cs`
- `src/Primitives.EntityFrameworkCore/Internal/IdConvention.cs`
- `src/Primitives.EntityFrameworkCore/ArchPillar.Extensions.Primitives.EntityFrameworkCore.csproj`

#### Why three layers and not just a `ValueConverter`

A property-level `ValueConverter` handles read/write for a mapped column. It does **not** cover:

1. **Constants on the right of an equality** — `where e.Id == Id<User>.New()` produces an unmapped constant; without a type mapping for `Id<User>`, EF can't pick a SQL type.
2. **`Contains` over collection parameters** — `ids.Contains(e.Id)` where `ids` is `IEnumerable<Id<User>>` needs a type mapping for the parameter array, not just the property.
3. **Joins through subqueries** — intermediate expressions can lose the property converter as they flow through the translator.
4. **`OrderBy`/`GroupBy`/`Distinct`** — change-tracker comparisons and projection equality rely on a `ValueComparer`. Without one, EF defaults to `object.Equals` and silently boxes/misbehaves.
5. **Function-call results / shadow properties** — anywhere `Id<T>` appears outside a directly mapped property, the type mapping source plugin is the only thing that registers it as a SQL type.

`IRelationalTypeMappingSourcePlugin` solves (1)–(3) and (5). `ValueComparer<Id<T>>` solves (4). `ValueConverter<Id<T>, Guid>` is what plugin and convention both delegate to.

#### Null and default semantics

`Id<T>` is a value type. Semantics match `Guid`:

- `default(Id<T>)` writes `00000000-0000-0000-0000-000000000000` to a non-nullable column — just like `default(Guid)`.
- `Id<T>?` (nullable wrapper) writes `NULL` for `null`, the wrapped Guid for a value — just like `Guid?`.
- No special-case "default → NULL" conversion. If a column should be nullable, use `Id<T>?`.

## Test Plan

The test plan is the *proof* the EF integration is sound. We do not ship a passing build with broken query shapes.

### `tests/Primitives.Tests/Identifiers/` — in-memory

| File | Coverage |
|---|---|
| `IdEqualityTests.cs` | `==`, `!=`, `Equals(object)`, `Equals(Id<T>)`, `GetHashCode` consistency, dictionary key, `Id<User>` vs `Id<Order>` distinct CLR types |
| `IdComparisonTests.cs` | `IComparable.CompareTo`, sort order matches `Guid` byte order |
| `IdFactoryTests.cs` | `New()` produces unique non-empty Guids; `default` and `Empty` both yield `Guid.Empty`; on net9+, version nibble is 7 and successive `New()` results sort in creation order |
| `IdConversionTests.cs` | Implicit `Id<T> → Guid` round-trips; explicit `Guid → Id<T>` works; raw `Guid` cannot implicitly become `Id<T>` |
| `IdFormattingTests.cs` | `ToString` with `N/D/B/P/X` formats; `TryFormat` into spans; `Parse`/`TryParse` round-trip |
| `IdJsonTests.cs` | Round-trips as a bare Guid string through `JsonSerializer`; rejects garbage; works on a wrapper DTO |

### `tests/Primitives.EntityFrameworkCore.Tests/` — real PostgreSQL

Mirrors `tests/Mapper.Tests/PostgresFixture` and `PostgresTestDatabase` for isolated databases. Each test class gets its own database via `[Collection("PostgreSQL")]`.

| File | Coverage |
|---|---|
| `IdRoundTripTests.cs` | Insert + read; column type is `uuid`; `Id<T>?` stores NULL when null |
| `IdEqualityQueryTests.cs` | `where e.Id == parameter`, `where e.Id == default`, `where e.OptionalFkId == null`, `where e.OptionalFkId != null` |
| `IdContainsQueryTests.cs` | `ids.Contains(e.Id)` with `IEnumerable<Id<User>>`, `List<Id<User>>`, empty collection |
| `IdJoinQueryTests.cs` | Join two entities on `Id<T>` PK ↔ FK; SQL contains a single equality; no client eval |
| `IdGroupingQueryTests.cs` | `GroupBy(e => e.OwnerId)`, `OrderBy(e => e.Id)`, `Distinct()` on `Id<T>` projection |
| `IdProjectionTests.cs` | `select new { e.Id }`, `select e.Id` materializes correctly |
| `IdConstantTests.cs` | `where e.Id == Id<User>.New()`, `where e.Id == (Id<User>)Guid.Parse("...")` |
| `IdConventionTests.cs` | Without `UseArchPillarTypedIds()`, queries fail or eval client-side; with it, all `IId` properties get the converter automatically |
| `HasIdConversionTests.cs` | Explicit per-property opt-in works without the auto-convention |
| `IdChangeTrackingTests.cs` | Modifying a tracked entity's `Id<T>` updates correctly; snapshot compares by `Value` (proves `ValueComparer` is wired) |

### Build expectations

- `dotnet build` produces zero warnings (warnings-as-errors per project policy).
- `dotnet test tests/Primitives.Tests` passes on all target frameworks.
- `dotnet test tests/Primitives.EntityFrameworkCore.Tests` passes against PostgreSQL (Testcontainers locally, host-local Postgres in `CLAUDE_CLOUD=true`).

## Project Wiring

- `src/Primitives.EntityFrameworkCore/ArchPillar.Extensions.Primitives.EntityFrameworkCore.csproj`
  - Multi-targets `net8.0;net9.0;net10.0` (EF Core 8/9/10 support these)
  - References `Microsoft.EntityFrameworkCore.Relational`
  - Project ref to `src/Primitives/`
  - Mirrors NuGet packaging metadata of `src/Mapper.EntityFrameworkCore/`
- `tests/Primitives.EntityFrameworkCore.Tests/ArchPillar.Extensions.Primitives.EntityFrameworkCore.Tests.csproj`
  - Adds `Microsoft.EntityFrameworkCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Testcontainers.PostgreSql`, `xunit`
  - Project refs to both Primitives projects
- Add both projects to the solution file.
- `Primitives.csproj` continues to be BCL-only (no EF dependency) — `Id<T>` and `IId` add no new package references.

## Open Questions

None outstanding — all decisions captured above.
