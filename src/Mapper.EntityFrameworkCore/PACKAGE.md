# ArchPillar.Extensions.Mapper.EntityFrameworkCore

EF Core integration for [`ArchPillar.Extensions.Mapper`](https://www.nuget.org/packages/ArchPillar.Extensions.Mapper). Opt in once per `DbContext` and your mappers become first-class citizens inside hand-written LINQ queries.

## What it adds

- **Direct `Map()` / `Project()` in your own queries** — call a mapper for a single property (or a child collection) inside a hand-written `Select`, and it is inlined into the mapper's projection expression so the whole query is still translated server-side.
- **Flat SQL `CASE` for enum mappers** — `EnumMapper.Map()` / `SymmetricEnumMapper.Map()` calls used directly in a query are translated to a single flat `CASE … WHEN … END` instead of a nested conditional chain.

## Quick Start

```csharp
// 1. Register the integration on the DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options
        .UseNpgsql(connectionString)
        .UseArchPillarMapper());

// 2. Call mappers directly inside a hand-written query — translated server-side
var summaries = await db.Orders
    .Where(o => o.Customer.UserId == userId)
    .Select(o => new OrderSummary
    {
        Id       = o.Id,
        Status   = mappers.OrderStatusCode.Map(o.Status),      // flat SQL CASE
        Customer = mappers.Customer.Map(o.Customer),           // single property via a mapper
        Lines    = o.Lines.Project(mappers.OrderLine).ToList(),  // collection via a mapper
    })
    .ToListAsync();
```

Whole-row projection (`db.Orders.Project(mappers.Order)`) works without this package — the core library is provider-agnostic. This package is for the cases above, where a mapper call sits *inside* a query you wrote by hand.

## Documentation

Full documentation and examples are available at the [GitHub repository](https://github.com/ArchPillar/extensions).
