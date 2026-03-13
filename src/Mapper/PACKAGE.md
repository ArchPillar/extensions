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
    public Mapper<Order, OrderDto> OrderToDto { get; set; } = null!;

    protected override void OnBuildingMappers(MapperContextBuilder builder)
    {
        builder.MapperFor(m => m.OrderToDto)
            .MapTo(dest => dest.Id,         src => src.Id)
            .MapTo(dest => dest.Total,      src => src.Total)
            .MapTo(dest => dest.CustomerName, src => src.Customer.Name);
    }
}

// 2. Use it — in memory
var mappers = new AppMappers();
OrderDto dto = mappers.OrderToDto.Map(order);

// 3. Use it — as IQueryable projection
var dtos = dbContext.Orders.Select(mappers.OrderToDto).ToList();
```

## Documentation

Full documentation and examples are available at the [GitHub repository](https://github.com/ArchPillar/mapper).
