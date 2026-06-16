# Mapper — EF Core integration

The core `ArchPillar.Extensions.Mapper` package is provider-agnostic: `Project(mapper)` and
`ToExpression()` emit plain expression trees that EF Core translates, with nothing extra to
install. The companion package **`ArchPillar.Extensions.Mapper.EntityFrameworkCore`** is opt-in
and adds two capabilities — direct mapper calls inside hand-written queries, and flat SQL `CASE`
for enum mappers. Add it only when you need those.

```bash
dotnet add package ArchPillar.Extensions.Mapper.EntityFrameworkCore
```

```csharp
// Program.cs
builder.Services.AddDbContext<AppDbContext>(options => options
    .UseNpgsql(connectionString)
    .UseArchPillarMapper());   // no arguments; returns the same builder for chaining
```

`UseArchPillarMapper` declares no mappers up front. Every mapper is resolved from the constant
it appears as in the query, and enum lookup tables are computed on demand the first time a given
mapper is used.

## Direct mapper calls in hand-written `Select`

`Project(mapper)` projects a whole row. When you instead want a custom `Select` and to map only
*one* property with a mapper, the companion package inlines the `Map()` / `Project()` call into
the mapper's projection expression at query-compilation time, so the whole query is still
translated server-side:

```csharp
var rows = await db.Orders
    .Where(o => o.IsActive)
    .Select(o => new OrderRow
    {
        OrderId       = o.Id,                                          // your own projection
        CustomerEmail = o.Customer.Email,                             // your own projection
        Order         = mappers.Order.Map(o),                         // one property via a mapper
        Lines         = o.Lines.Project(mappers.OrderLine).ToList(),  // a collection via a mapper
    })
    .ToListAsync();
```

Scalar `Map()` and collection `Project()` are both inlined; nested mappers, enum mappers, and
variables inside them resolve exactly as for a top-level `Project(mapper)`.

This is **mix mode** — your own projection mixed with mapper calls. Two ways to inline it: the
automatic `IQueryExpressionInterceptor` enabled by `UseArchPillarMapper()`, or an explicit
`ApplyMappers()` call on the query (see below).

> **Recommendation: prefer the explicit `ApplyMappers()` helper for mix-mode queries.** The
> automatic interceptor works for the common case, but it runs *after* EF Core's parameter
> extraction, which breaks the one combination below. Inlining manually with `ApplyMappers()`
> avoids that edge case entirely and makes the inlining point visible in the query.

> `Map()` / `Project()` used **inside a mapper definition** are always inlined by the core
> library and never need this package. The companion is only for direct calls inside your own
> queries.

## The edge case that bites: mix mode + `Invoke`

**Mix mode combined with inline escaping (`Invoke`) only works if `ApplyMappers()` is called.**
The automatic interceptor runs *after* EF Core's parameter extraction, so it cannot inline a
mapper that contains an `Invoke` call (a nested mapping that deliberately opted out of
expression-tree inlining) — the invoke box would be spliced in too late to be lifted into a
query parameter, and EF rejects it as a captured constant. The interceptor detects this and
throws a clear error pointing here.

The fix — and the reason to prefer it for all mix-mode queries — is `ApplyMappers()`, the
explicit counterpart that runs the same rewrite at query-construction time, *before* parameter
extraction, so the box (and any variable boxes) parameterize normally:

```csharp
using ArchPillar.Extensions.Mapper.EntityFrameworkCore;

var rows = await db.Orders
    .Select(o => new OrderRow { OrderId = o.Id, Order = mappers.Order.Map(o)! })
    .ApplyMappers()   // inline before EF sees the query → Invoke-containing mappers work
    .ToListAsync();
```

Whole-row `.Project(mapper)` already runs at construction time, so it supports
`Invoke`-containing mappers without the explicit call.

## Requesting optional properties at the call site

The plain `Map(src)` / `Project(mapper)` overloads project required properties only. To pull in
optionals, use the options overloads from the
`ArchPillar.Extensions.Mapper.EntityFrameworkCore` namespace:

```csharp
using ArchPillar.Extensions.Mapper.EntityFrameworkCore;

db.Orders.Select(o => mappers.Order.Map(o, c => c.Include(m => m.CustomerName)));

db.Orders.Select(o => new OrderRow
{
    OrderId = o.Id,
    Lines   = o.Lines.Project(mappers.OrderLine, c => c.Include(m => m.SupplierName)).ToList(),
});
```

## Enum mappers → flat SQL `CASE`

`EnumMapper.Map()` and `SymmetricEnumMapper.Map()` / `.MapReverse()` used directly in a query
translate to a flat `CASE … WHEN … END` with one branch per enum value, instead of the nested
conditional chain the provider-agnostic path emits. This keeps SQL nesting shallow — important
for providers with nesting limits and enums with many values:

```csharp
db.Orders.Select(o => mappers.OrderStatusMapper.Map(o.Status)); // → CASE o.Status WHEN 0 THEN … END
```

## Notes / gotchas

- The integration resolves mappers through the `context.Mapper` access pattern (the same scan
  used for registered contexts). Mappers reached only through a non-`MapperContext` facade are
  **not** recognized.
- Variable values bound at an inline call site bake in as constants captured at compile time;
  prefer `Include()` for optional properties over `Set()` for compile-time-stable behavior.
- **Match EF Core major versions.** This package hooks EF Core internals, so its EF Core
  dependency must align with the EF Core major version your app uses. A mismatch (e.g. mixing an
  EF Core 9 and an EF Core 10 assembly) surfaces as a `MissingMethodException` at *query time*,
  not at compile time — keep the `Microsoft.EntityFrameworkCore.*` packages on one major.
