# Npgsql Improvements — Spec Sheet (Tier 1: pure, extractable)

Provider-level fixes/extensions to `Npgsql` + `Npgsql.EntityFrameworkCore.PostgreSQL`
that carry no domain concepts and are safe to ship as a standalone library.

## Wiring seams

| Seam | Interface / hook | Registered via | Purpose |
|---|---|---|---|
| ADO wire | `PgTypeInfoResolverFactory` | `NpgsqlDataSourceBuilder.AddTypeInfoResolverFactory(...)` | binary read/write of values (parameters + column reads) |
| EF type mapping | `IRelationalTypeMappingSourcePlugin` | `IDbContextOptionsExtension.ApplyServices` | store type + **SQL literal generation** |
| EF query translation | `IMethodCallTranslatorPlugin` | `IDbContextOptionsExtension.ApplyServices` | translate method calls → SQL |

> **Layering note:** the ADO-wire converters (`PgTypeInfoResolverFactory`) handle *how a value is serialized over the protocol* (parameters + column reads). The EF type mappings handle *store type + how a constant literal is written into SQL text*. Different layers — the Guid `::uuid` fix lives at the EF-literal layer, not the wire layer. The library keeps them in separate folders (`Internal/Converters` vs `Internal/TypeMappings`) for the same reason.

---

## 1. Guid `uuid` literal cast — `'…'::uuid`

- **Problem.** Npgsql renders a `Guid` *constant literal* as a bare `'57afda40-…'`, which PostgreSQL types as `text`. Fine in comparisons (PG coerces `text → uuid`), but a uuid constant **projected as a read column** comes back as `text`:
  ```
  InvalidCastException: Reading as 'System.Guid' is not supported for fields having DataTypeName 'text'.
  ```
- **Mechanism.** Custom `RelationalTypeMapping` overriding `GenerateNonNullSqlLiteral` → `'…'::uuid`. Literal-only; parameters and columns are untouched (they bind via `NpgsqlDbType.Uuid`).
- **Files.** `Internal/TypeMappings/GuidUuidMapping.cs`, `Internal/TypeMappings/GuidUuidTypeMappingSourcePlugin.cs`.

## 2. `DateTimeOffset → timestamptz`, always UTC

- **Problem.** Deterministic UTC storage regardless of the input offset, plus `DateTimeOffset.Min/MaxValue ↔ Postgres ±infinity`.
- **Mechanism.** `PgBufferedConverter<DateTimeOffset>` — `WriteCore` calls `value.UtcDateTime` then clamps `MinValue/MaxValue` to `long.Min/Max`. `ReadCore` decodes microseconds-since-2000 and clamps back. Epoch / precision math lives in `PgTimestamp`.
- **Files.** `Internal/Converters/DateTimeOffsetTimestampTzConverter.cs`, `Internal/Converters/PgTimestamp.cs`.

## 3. `DateTime → timestamptz`, UTC, no `Unspecified`

- **Problem.** Enforce UTC; reject `DateTimeKind.Unspecified` (ambiguous); min/max ↔ infinity.
- **Mechanism.** `PgBufferedConverter<DateTime>` — `Local → UTC`, throws on `Unspecified`, reads back as `UtcDateTime`.
- **File.** `Internal/Converters/DateTimeTimestampTzConverter.cs`.

## 4. CLR `enum ↔ int4`

- **Problem.** Npgsql doesn't map arbitrary CLR enums to integer columns out of the box.
- **Mechanism.** `EnumInt4Converter<TEnum>` (`PgBufferedConverter`) maps `enum ↔ int` via prebuilt `FrozenDictionary` lookups. Unknown integers read back as `default(TEnum)`.
- **File.** `Internal/Converters/EnumInt4Converter.cs`, wired via `Internal/Converters/ArchPillarTypeInfoResolverFactory.cs`.
- **Note.** unknown→`default` is silent. A future revision may prefer to throw — change this when it bites someone.

## 5. `EF.Functions.JsonbBuildObject(…) → jsonb_build_object(…)`

- **Problem.** No first-class way to build a `jsonb` object in LINQ; also EF's tree validator requires every node to carry a type mapping.
- **API.** Two shapes. Fixed-arity `EF.Functions.JsonbBuildObject(k, v, …)` overloads (1–3 pairs) for concise inline use, and a fluent `EF.Functions.JsonbObject(k, v).Add(k, v)….Build()` builder for arbitrary arity with compile-time-typed string keys. The builder's first pair lives on the `JsonbObject(...)` seed so the call references query data — an empty constant seed is funcletized (evaluated client-side) before translation. `.Build()` is the required terminal: it yields a `string` result type so EF can materialise the projection (a bare builder-typed projection can't be materialised from a scalar).
- **Mechanism.** Matches every `JsonbBuildObjectDbFunctions` marker by name. The builder chain is folded one node at a time as EF translates bottom-up — each `Add` appends its prepared key/value to the accumulated `jsonb_build_object(...)`. Strips EF's boxing `Convert(..., object)` wrappers, forces a `text` mapping onto any mapping-less node (incl. compound `CASE`/`COALESCE`), emits `SqlFunctionExpression("jsonb_build_object", …)` with a `jsonb` mapping on the return. No dependency on Npgsql internals.
- **Files.** `Internal/Functions/JsonbBuildObjectTranslator.cs`, `Internal/Functions/JsonbBuildObjectMethodCallTranslatorPlugin.cs`. The `EF.Functions` markers and `IJsonbObjectBuilder` live at `JsonbBuildObjectDbFunctions.cs` / `IJsonbObjectBuilder.cs` and travel with the library.

---

## Dependencies

These are **leaf infrastructure** with no domain coupling:

| Component | `Npgsql.Internal` dependency |
|---|---|
| `PgTimestamp` | none |
| `EnumInt4Converter` | `PgBufferedConverter`, `PgReader`/`PgWriter` |
| `DateTimeOffsetTimestampTzConverter` | `PgBufferedConverter`, `PgReader`/`PgWriter` |
| `DateTimeTimestampTzConverter` | `PgBufferedConverter`, `PgReader`/`PgWriter` |
| `ArchPillarTypeInfoResolverFactory` | `PgTypeInfoResolverFactory`, `IPgTypeInfoResolver`, `TypeInfoMappingCollection`, `TypeInfoMappingHelpers` |
| `GuidUuidMapping` | EF Core's `RelationalTypeMapping` (public); Npgsql's `NpgsqlDbType` (public) for parameter binding |
| `JsonbBuildObjectTranslator` | EF Core's `IMethodCallTranslator` (public) only — no Npgsql internals |

**Fragility note.** The wire converters depend on Npgsql `*.Internal` namespaces. These are not part of Npgsql's public API and can change across major versions — pin/track versions deliberately. The library targets Npgsql 8 / 9 / 10 against EF Core 8 / 9 / 10. CI exercises all three.

The project sets `<NoWarn>$(NoWarn);EF1001</NoWarn>` as a guard for any future use of EF/Npgsql internal query-pipeline APIs by this provider extension; the diagnostic is aimed at application code that shouldn't reach for EF internals.

---

## Cross-cutting design principle

Prefer **parameters over baked constants** for dynamic values. The `::uuid` literal (item 1) is the safety net for cases where a constant is genuinely unavoidable; parameterisation is the deeper fix and avoids the literal-typing problem on every provider.
