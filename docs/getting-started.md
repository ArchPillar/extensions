# Getting Started

## Installation

Add the NuGet package to your project:

```bash
dotnet add package ArchPillar.Mapper
```

> Requires .NET 9 and C# 13.

## Your First Mapper

Create a context class that inherits from `MapperContext`. Declare mappers as public properties and initialize them in the constructor.

```csharp
using ArchPillar.Mapper;

// Source model
public class Order
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Status { get; set; }
    public List<OrderLine> Lines { get; set; }
}

public class OrderLine
{
    public string ProductName { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

// Destination DTOs
public class OrderDto
{
    public required int Id { get; set; }
    public required DateTime PlacedAt { get; set; }
    public required string Status { get; set; }
    public required List<OrderLineDto> Lines { get; set; }
}

public class OrderLineDto
{
    public required string ProductName { get; set; }
    public required int Quantity { get; set; }
    public required decimal UnitPrice { get; set; }
}
```

```csharp
public class AppMappers : MapperContext
{
    public Mapper<OrderLine, OrderLineDto> OrderLine { get; }
    public Mapper<Order, OrderDto> Order { get; }

    public AppMappers()
    {
        OrderLine = CreateMapper<OrderLine, OrderLineDto>(src => new OrderLineDto
        {
            ProductName = src.ProductName,
            Quantity    = src.Quantity,
            UnitPrice   = src.UnitPrice,
        });

        Order = CreateMapper<Order, OrderDto>(src => new OrderDto
        {
            Id       = src.Id,
            PlacedAt = src.CreatedAt,
            Status   = src.Status,
            Lines    = src.Lines.Project(OrderLine).ToList(),
        });
    }
}
```

A few things to note:

- **Leaf mappers first.** `OrderLine` is declared before `Order` because `Order` references it. The library resolves nested mapper references lazily, so declaration order doesn't actually matter at runtime — but declaring leaves first keeps the code readable.
- **`Project(OrderLine).ToList()`** maps a collection. The expression visitor inlines `OrderLine`'s expression into `Order`'s expression tree, so LINQ providers see a single flat expression — no delegate calls, no client-side evaluation.
- **Implicit `Build()`.** Assigning a `MapperBuilder` to a `Mapper` property triggers `.Build()` via the implicit conversion operator. You can also call `.Build()` explicitly if you prefer.

## Using the Mapper

### In-memory mapping

```csharp
var mappers = new AppMappers();

OrderDto dto = mappers.Order.Map(order);
```

### EF Core projection

```csharp
var results = await dbContext.Orders
    .Where(o => o.IsActive)
    .Project(mappers.Order)
    .ToListAsync();
```

Both use the same mapper definition. The LINQ provider sees a plain expression tree.

## Dependency Injection

Register the context as a singleton — mappers are thread-safe and share compiled delegates via `Lazy<T>`.

```csharp
// Program.cs
builder.Services.AddSingleton<AppMappers>();
```

Then inject it wherever you need mapping:

```csharp
public class OrderService(AppMappers mappers)
{
    public async Task<List<OrderDto>> GetOrdersAsync(AppDbContext db)
    {
        return await db.Orders
            .Project(mappers.Order)
            .ToListAsync();
    }
}
```

## Eager Compilation

By default, mappers compile lazily on first use. To surface mapping errors at startup and eliminate cold-start latency, call `EagerBuildAll()` at the end of the constructor:

```csharp
public class AppMappers : MapperContext
{
    public Mapper<OrderLine, OrderLineDto> OrderLine { get; }
    public Mapper<Order, OrderDto> Order { get; }

    public AppMappers()
    {
        OrderLine = CreateMapper<OrderLine, OrderLineDto>(src => new OrderLineDto
        {
            ProductName = src.ProductName,
            Quantity    = src.Quantity,
            UnitPrice   = src.UnitPrice,
        });

        Order = CreateMapper<Order, OrderDto>(src => new OrderDto
        {
            Id       = src.Id,
            PlacedAt = src.CreatedAt,
            Status   = src.Status,
            Lines    = src.Lines.Project(OrderLine).ToList(),
        });

        EagerBuildAll(); // Compile all mappers now
    }
}
```

This forces expression assembly and delegate compilation for every `Mapper` and `EnumMapper` on the context. Any configuration mistake (unmapped property, circular reference) throws immediately at construction time instead of at first use.

## Coverage Validation

By default, every non-nullable destination property must be explicitly mapped, marked optional, or ignored. If you miss one, `Build()` throws an `InvalidOperationException` listing the unmapped properties.

```csharp
// This throws — UnitPrice is not mapped
OrderLine = CreateMapper<OrderLine, OrderLineDto>()
    .Map(d => d.ProductName, s => s.ProductName)
    .Map(d => d.Quantity, s => s.Quantity);
// InvalidOperationException: "The following properties of OrderLineDto are not mapped: UnitPrice"
```

See [Features Guide — Coverage Validation](features.md#coverage-validation) for the available validation modes.

## Next Steps

- [Features Guide](features.md) — nested mappers, optionals, variables, enums, MapTo, and more
- [Recommendations](recommendations.md) — patterns, pitfalls, and production guidance
- [Specification](../SPEC.md) — full design philosophy and API surface
