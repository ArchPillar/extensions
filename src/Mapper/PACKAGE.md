# ArchPillar.Extensions.Mapper

A .NET library for explicit object-to-object DTO mapping and LINQ/EF Core expression projection. One mapper definition drives both in-memory object mapping and `IQueryable` projection — and every mapper is a named C# property, so Go to Definition and Find All References work the same as any other code.

## Why?

AutoMapper is everywhere, and for good reason — it solves a real problem. But its convention-based, implicit nature creates a different one: when a mapping breaks, you cannot trace it. A renamed property silently stops mapping, a misconfigured profile produces wrong data instead of a compile error, and "Find All References" on a DTO property turns up nothing because the mapping is invisible.

ArchPillar.Extensions.Mapper takes the opposite approach:

- **Every mapping is explicit** — no convention-based auto-discovery, no magic.
- **Every mapper is a named C# property** — fully navigable by IDE tooling.
- **Unmapped properties are build-time errors** — silence is not acceptance.
- **One definition, two modes** — the same mapper produces both compiled delegates and LINQ expression trees, with no client-side evaluation and no N+1.

The core library is provider-agnostic and depends only on BCL types. An opt-in companion package, `ArchPillar.Extensions.Mapper.EntityFrameworkCore`, adds EF Core query-time support — see [EF Core integration](#ef-core-integration) below.

## Quick Start

```csharp
// 1. Define your mapper context (like EF Core's DbContext)
public class AppMappers : MapperContext
{
    public Variable<int> CurrentUserId { get; } = CreateVariable<int>();

    public Mapper<OrderLine, OrderLineDto> OrderLine { get; }
    public Mapper<Order, OrderDto> Order { get; }

    public AppMappers()
    {
        OrderLine = CreateMapper<OrderLine, OrderLineDto>(src => new OrderLineDto
        {
            ProductName = src.Product.Name,
            Quantity    = src.Quantity,
            UnitPrice   = src.UnitPrice,
        });

        Order = CreateMapper<Order, OrderDto>(src => new OrderDto
        {
            Id      = src.Id,
            Status  = src.Status.ToString(),
            IsOwner = src.OwnerId == CurrentUserId,
            Lines   = src.Lines.Project(OrderLine).ToList(),
        })
        .Optional(dest => dest.CustomerName, src => src.Customer.Name);

        EagerBuildAll();
    }
}

// 2. Use it — in memory
var mappers = new AppMappers();
OrderDto dto = mappers.Order.Map(order);

// 3. Use it — as IQueryable projection (single SQL query, no N+1)
var dtos = await dbContext.Orders
    .Where(o => o.IsActive)
    .Project(mappers.Order, o => o
        .Include(m => m.CustomerName)
        .Set(mappers.CurrentUserId, currentUser.Id))
    .ToListAsync();
```

Both paths use the same definition. The LINQ provider sees a plain expression tree — no delegate calls, no client-side evaluation.

## Mapping styles

Declare properties with a member-init expression, a fluent `.Map()` chain, or a mix of both. Coverage validation ensures every destination property is mapped, optional, or ignored, throwing at build time if one is missed.

```csharp
Order = CreateMapper<Order, OrderDto>(src => new OrderDto
{
    Id       = src.Id,
    PlacedAt = src.CreatedAt,
    Lines    = src.Lines.Project(OrderLine).ToList(), // nested mapper, inlined
})
.Map(dest => dest.Status, src => src.Status.ToString())
.Optional(dest => dest.CustomerName, src => src.Customer.Name); // opt-in
```

Nested mapper calls (`Map(src.X)` for single objects, `src.X.Project(mapper)` for collections) are inlined into the parent expression at build time. Enum-to-enum mapping (`EnumMapper<,>`, `SymmetricEnumMapper<,>`), shallow cloning (`CreateCloneMapper<T>()`), destination-hierarchy reuse (`Inherit(...).For<T>()`), in-place updates (`MapTo`), and expression transformers are all supported.

## Runtime variables and optionals

`Variable<T>` properties are typed expression placeholders bound at the call site — no magic strings. Optional properties are requested per call via typed `Include()` or string paths, mirroring EF Core's `Include` / `ThenInclude`:

```csharp
mappers.Order.Map(order, o => o.Set(mappers.CurrentUserId, userId));

query.Project(mappers.Order, o => o
    .Include(m => m.CustomerName)
    .Include("Lines.SupplierName"));
```

## EF Core integration

`Project(mapper)` and `ToExpression()` emit plain expression trees that EF Core translates directly — no extra package needed for whole-row projection. The opt-in companion package [`ArchPillar.Extensions.Mapper.EntityFrameworkCore`](https://www.nuget.org/packages/ArchPillar.Extensions.Mapper.EntityFrameworkCore) adds query-time support, registered once on the `DbContextOptionsBuilder`:

```csharp
builder.Services.AddDbContext<AppDbContext>(options =>
    options
        .UseNpgsql(connectionString)
        .UseArchPillarMapper());
```

It inlines direct `Map()` / `Project()` calls made inside a hand-written `Select`, and translates enum mapper calls to a flat SQL `CASE`. The core library stays provider-agnostic and dependency-free.

## Documentation

Full documentation, features, and examples are in the
[GitHub repository](https://github.com/ArchPillar/extensions/tree/main/docs/mapper).

## License

MIT — see the bundled `LICENSE` file.
