# ArchPillar.Extensions.EntityFrameworkCore.Npgsql

Provider-level fixes and extensions for [`Npgsql`](https://www.nuget.org/packages/Npgsql) and [`Npgsql.EntityFrameworkCore.PostgreSQL`](https://www.nuget.org/packages/Npgsql.EntityFrameworkCore.PostgreSQL). Opt in once at the `NpgsqlDataSourceBuilder` and once at the `DbContextOptionsBuilder` and your queries are protected from the most common Npgsql foot-guns.

## What it adds

| # | Feature | Layer |
| --- | --- | --- |
| 1 | `Guid` constants are emitted as `'…'::uuid` literals, so a `Guid` constant projected as a read column comes back as `Guid` instead of throwing `InvalidCastException: Reading as 'System.Guid' is not supported for fields having DataTypeName 'text'`. | EF type mapping |
| 2 | `DateTimeOffset ↔ timestamptz`, always stored as the UTC instant regardless of the input offset. `MinValue`/`MaxValue` map to PostgreSQL `±infinity`. | ADO wire |
| 3 | `DateTime ↔ timestamptz`, UTC-enforcing — `Utc` and `Local` are accepted (Local converted to UTC), `Unspecified` is rejected with a clear error instead of silently storing the wrong instant. `MinValue`/`MaxValue` map to `±infinity`. | ADO wire |
| 4 | Any CLR `enum` ↔ PostgreSQL `int4` (no enum column type required). Unknown integers read back as `default(TEnum)`. | ADO wire |
| 5 | `EF.Functions.JsonbBuildObject(key1, val1, key2, val2, …)` translates to `jsonb_build_object(…)`. Boxing `Convert(...)` wrappers around values are stripped and a `text` type mapping is applied to any mapping-less argument so the function can be used inside a projection. | EF query translator |

## Quick Start

```csharp
using ArchPillar.Extensions.EntityFrameworkCore.Npgsql;
using Microsoft.EntityFrameworkCore;
using Npgsql;

// 1. ADO wire converters live on the data source.
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.UseArchPillarNpgsqlImprovements();
await using var dataSource = dataSourceBuilder.Build();

// 2. EF-side fixes (Guid literal cast, EF.Functions.JsonbBuildObject) hook
//    into the DbContextOptions pipeline.
builder.Services.AddDbContext<AppDbContext>(options =>
    options
        .UseNpgsql(dataSource)
        .UseArchPillarNpgsqlImprovements());

// 3. Write LINQ the way you'd expect.
var summaries = await db.Tickets
    .Select(t => new
    {
        t.Id,                                         // Guid round-trip even when projected as a constant elsewhere
        OpenedAt = t.OpenedAt,                        // always UTC on the way out
        Severity = (int)t.Severity,                   // enum stored as int4
        Json = EF.Functions.JsonbBuildObject(         // jsonb_build_object(...)
            "id", t.Id,
            "title", t.Title,
            "severity", (int)t.Severity),
    })
    .ToListAsync();
```

You can call either extension on its own. The data source extension covers the wire path; the `DbContextOptionsBuilder` extension covers the EF-side fixes. Most apps want both.

## Cross-cutting design principle

Prefer **parameters over baked constants** for dynamic values. The `'…'::uuid` literal (feature 1) is the safety net for cases where a constant is genuinely unavoidable; parameterisation is the deeper fix and avoids the literal-typing problem on every provider.

## Documentation

Full documentation and examples live at the [GitHub repository](https://github.com/ArchPillar/extensions).
