# ArchPillar.Extensions.Mapper

A .NET library for explicit object-to-object DTO mapping and LINQ/EF Core expression projection.

## Why?

- **Traceable** — every mapper is a named property on a concrete class; Go to Definition and Find All References just work.
- **Explicit** — no convention-based auto-mapping. Every mapped property is declared.
- **Dual mode** — one definition drives both in-memory object mapping and IQueryable projection.
- **Composable** — nested model mappers are reused automatically.
- **Type-safe** — unmapped destination properties cause a build-time exception, not a runtime surprise.

## Quick Start

```csharp
// 1. Define your mapper context (like EF Core's DbContext)
public class AppMappers : MapperContext
{
    public Mapper<Address, AddressDto> Address { get; }
    public Mapper<OrderLine, OrderLineDto> OrderLine { get; }
    public Mapper<Order, OrderDto> Order { get; }

    public AppMappers()
    {
        // Single-object mapper
        Address = CreateMapper<Address, AddressDto>(src => new AddressDto
        {
            Street  = src.Street,
            City    = src.City,
            Country = src.Country,
        });

        // Child collection mapper
        OrderLine = CreateMapper<OrderLine, OrderLineDto>(src => new OrderLineDto
        {
            ProductName = src.ProductName,
            Quantity    = src.Quantity,
            UnitPrice   = src.UnitPrice,
        });

        // Parent mapper — nests both:
        //   Map()     for a single object (Address)
        //   Project() for a collection    (OrderLine)
        // Both are inlined automatically, fully translatable by EF Core
        Order = CreateMapper<Order, OrderDto>(src => new OrderDto
        {
            Id           = src.Id,
            CustomerName = src.Customer.Name,
            Total        = src.Total,
            Address      = Address.Map(src.ShippingAddress),
            Lines        = src.Lines.Project(OrderLine).ToList(),
        });
    }
}

// 2. Use it — in memory
var mappers = new AppMappers();
OrderDto dto = mappers.Order.Map(order);

// 3. Use it — as IQueryable projection (single SQL query, no N+1)
var dtos = dbContext.Orders.Project(mappers.Order).ToList();
```

## Documentation

Full documentation and examples are available at the [GitHub repository](https://github.com/ArchPillar/mapper).
