# ArchPillar.Extensions.EntityFrameworkCore.Npgsql

Provider-level fixes and extensions for [`Npgsql`](https://www.nuget.org/packages/Npgsql) and [`Npgsql.EntityFrameworkCore.PostgreSQL`](https://www.nuget.org/packages/Npgsql.EntityFrameworkCore.PostgreSQL). The package's assembly base namespace is `ArchPillar.Extensions.EntityFrameworkCore.Npgsql`. Internals live under `…Internal` and are not part of the public surface.

This document covers the public API surface, how to register the integration, and a brief description of each Tier 1 feature. The full design rationale lives in [SPEC.md](SPEC.md).

## Public surface

| Type / Method | Purpose |
| --- | --- |
| `NpgsqlImprovementsDataSourceBuilderExtensions.UseArchPillarNpgsqlImprovements(this NpgsqlDataSourceBuilder)` | Installs the ADO-wire converters on a data source builder. |
| `NpgsqlImprovementsDbContextOptionsExtensions.UseArchPillarNpgsqlImprovements(this DbContextOptionsBuilder)` | Registers the EF Core type-mapping and method-call-translator plugins on a `DbContext`. |
| `NpgsqlImprovementsDbContextOptionsExtensions.UseArchPillarNpgsqlImprovements<TContext>(this DbContextOptionsBuilder<TContext>)` | Generic overload that preserves the `TContext` type parameter. |
| `JsonbDbFunctions.ToJsonb(this DbFunctions, TShape shape)` | LINQ-translatable helper for PostgreSQL `jsonb_build_object(...)`; keys come from the shape's member names. |

## Wiring

```csharp
using ArchPillar.Extensions.EntityFrameworkCore.Npgsql;
using Microsoft.EntityFrameworkCore;
using Npgsql;

// 1. ADO wire converters live on the data source.
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.UseArchPillarNpgsqlImprovements();
await using var dataSource = dataSourceBuilder.Build();

// 2. EF-side fixes hook into the DbContextOptions pipeline.
services.AddDbContext<AppDbContext>(options =>
    options
        .UseNpgsql(dataSource)
        .UseArchPillarNpgsqlImprovements());
```

The two registrations are independent — you can install just the data source converters in a non-EF application, or just the EF plugins if you only need the Guid `::uuid` and `ToJsonb` fixes. Most apps want both.

## What it adds

### 1. Guid `'…'::uuid` literal cast — fixes Guid projection

Npgsql renders a `Guid` constant as a bare `'57afda40-…'` SQL literal, which PostgreSQL types as `text`. That's fine in `WHERE` comparisons (PG coerces text to uuid), but a uuid constant **projected as a read column** comes back as `text`:

```
InvalidCastException: Reading as 'System.Guid' is not supported for fields having DataTypeName 'text'.
```

This integration installs a custom `RelationalTypeMapping` for `Guid` that overrides `GenerateNonNullSqlLiteral` to emit `'…'::uuid`. Parameters and stored columns are untouched — they bind via the existing `NpgsqlDbType.Uuid` path.

```csharp
var stamp = Guid.NewGuid();
var rows = await db.Tickets
    .Select(t => new { t.Id, RequestStamp = stamp })
    .ToListAsync();          // works — both columns come back as Guid
```

### 2. `DateTimeOffset → timestamptz`, always UTC

Stores the UTC instant regardless of the input offset, and round-trips `MinValue`/`MaxValue` through PostgreSQL `±infinity`. Reads always come back with `Offset = TimeSpan.Zero`.

```csharp
// Both rows land at the same instant in the database; both come back as UTC.
ctx.Tickets.Add(new Ticket { OccurredAt = new DateTimeOffset(utc, TimeSpan.Zero) });
ctx.Tickets.Add(new Ticket { OccurredAt = new DateTimeOffset(local, TimeSpan.FromHours(2)) });
```

### 3. `DateTime → timestamptz`, UTC-enforcing

Accepts `DateTimeKind.Utc` (stored as-is) and `DateTimeKind.Local` (converted to UTC), and **rejects `DateTimeKind.Unspecified`** with a clear error. Reads always come back as `DateTimeKind.Utc`. `MinValue`/`MaxValue` map to `±infinity`.

This catches a class of "we accidentally stored local time as UTC" bugs at write time rather than years later when timezones surface.

### 4. CLR `enum ↔ int4`

Any CLR `enum` is bound to an `integer` column without any special schema. Read/write use prebuilt dictionaries; unknown integers read back as `default(TEnum)`.

```csharp
public enum Severity { Low = 1, Medium = 5, High = 9, Critical = 99 }

// Stores as int4; comparable, indexable, foreign-key-able.
public sealed class Ticket
{
    public Severity Severity { get; set; }
}
```

If you'd rather use PostgreSQL native enum types or `varchar` storage, register those as usual — this integration only ships the int4 path.

### 5. `EF.Functions.ToJsonb(shape) → jsonb_build_object(...)`

Build a PostgreSQL `jsonb` object from an object shape. The keys are the member names; the values are the member values:

```csharp
var json = await ctx.Tickets
    .Select(t => EF.Functions.ToJsonb(new
    {
        id       = t.Id,
        title    = t.Title,
        severity = (int)t.Severity,
    }))
    .ToListAsync();
// → SELECT jsonb_build_object('id', t.Id, 'title', t.Title, 'severity', t.Severity) FROM …
```

`shape` must be an **inline object initializer** — an anonymous type (`new { … }`) or a named type with an object initializer (`new MyDto { A = …, B = … }`). Member names are read off the expression tree at query-compilation time and become SQL string literals; the shape object is never instantiated and **no reflection runs per row**. Positional constructors/records (`new Foo(a, b)`) and pre-built instances (`ToJsonb(someDto)`) aren't supported — they carry no per-member expressions — and produce a clear error.

Under the hood a query-compilation interceptor desugars the shape into `jsonb_build_object('k', v, …)`; the translator strips EF's boxing `Convert(..., object)` wrappers and applies a `text` type mapping to any mapping-less argument (e.g. compound `CASE`/`COALESCE` nodes) so the function works in projections without the EF tree validator complaining. Direct invocation throws — the method is a translator marker.

## Cross-cutting design principle

Prefer **parameters over baked constants** for dynamic values. The `'…'::uuid` literal (feature 1) is the safety net for cases where a constant is genuinely unavoidable; parameterisation is the deeper fix and avoids the literal-typing problem on every provider.

## Fragility note

The wire converters and `GuidUuidMapping` depend on Npgsql `*.Internal` namespaces (`PgBufferedConverter`, `RelationalTypeMapping`, `PgNewArrayExpression`). These are not part of Npgsql's public API and can change across major versions, so pin/track versions deliberately. The library currently targets Npgsql 8 / 9 / 10 against EF Core 8 / 9 / 10 and CI exercises all three.

## See also

- [SPEC.md](SPEC.md) — design rationale and component-level extraction notes.
- The sample app at `samples/EntityFrameworkCore.Npgsql/Npgsql.Sample` exercises every feature against a live PostgreSQL.
