# ArchPillar.Mapper

A lightweight .NET mapping library built on expression trees. One definition drives both in-memory object mapping and LINQ/EF Core projection — with full IDE traceability.

## Why?

AutoMapper is everywhere, and for good reason — it solves a real problem. But its convention-based, implicit nature creates a different problem: **when something breaks, you can't trace it.**

A renamed property silently stops mapping. A misconfigured profile produces wrong data instead of a compile error. "Find All References" on a DTO property returns nothing useful because the mapping is invisible. These bugs are subtle, hard to reproduce, and expensive to find.

ArchPillar.Mapper takes the opposite approach:

- **Every mapping is explicit** — no convention-based auto-discovery, no magic
- **Every mapper is a named C# property** — Go to Definition and Find All References work exactly as expected
- **Unmapped properties are build-time errors** — silence is not acceptance
- **One definition, two modes** — the same mapper produces both compiled delegates and LINQ expression trees

## Quick Start

```csharp
public class AppMappers : MapperContext
{
    public Variable<int> CurrentUserId { get; } = CreateVariable<int>();

    public Mapper<Order, OrderDto> Order { get; }
    public Mapper<OrderLine, OrderLineDto> OrderLine { get; }

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
```

**In-memory mapping:**

```csharp
OrderDto dto = mappers.Order.Map(order);

// With a runtime variable
OrderDto dto = mappers.Order.Map(order, o => o.Set(mappers.CurrentUserId, userId));
```

**EF Core projection:**

```csharp
var results = await dbContext.Orders
    .Where(o => o.IsActive)
    .Project(mappers.Order, o => o
        .Include(m => m.CustomerName)
        .Set(mappers.CurrentUserId, currentUser.Id))
    .ToListAsync();
```

Both paths use the same mapper definition. The LINQ provider sees a plain expression tree — no delegate calls, no client-side evaluation, no N+1.

## Features

| Feature | Description |
|---------|-------------|
| **Dual-mode mapping** | Same definition compiles to both a delegate and an expression tree |
| **Nested mapper inlining** | `Map()` and `Project()` calls are inlined into the parent expression at build time |
| **Optional properties** | Opt-in properties with typed `Include()` or string-path `Include("Lines.Product")` |
| **Runtime variables** | Typed `Variable<T>` properties substituted at call time — no magic strings |
| **Enum mapping** | `EnumMapper<TSource, TDest>` generates EF Core-translatable conditional expressions |
| **MapTo** | Update an existing object in-place (zero allocation for scalar properties) |
| **Coverage validation** | Unmapped destination properties throw at build time, not at runtime |
| **Eager compilation** | `EagerBuildAll()` front-loads all compilation at startup |
| **Circular reference detection** | Self-referencing mapper chains throw a clear error instead of stack overflow |
| **Thread-safe** | Mappers use `Lazy<T>` and are safe to share as singletons |

## Performance

// * Summary *

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.7840/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 5900X 3.70GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.200
  [Host]   : .NET 9.0.14 (9.0.14, 9.0.1426.11910), X64 RyuJIT x86-64-v3
  ShortRun : .NET 9.0.14 (9.0.14, 9.0.1426.11910), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1
WarmupCount=3
```

| Method                                | Mean       | Error       | StdDev    | Gen0   | Allocated |
|-------------------------------------- |-----------:|------------:|----------:|-------:|----------:|
| Empty→Empty                           |   5.113 ns |   5.1559 ns | 0.2826 ns | 0.0014 |      24 B |
| '1 property'                          |   5.956 ns |   4.7004 ns | 0.2576 ns | 0.0014 |      24 B |
| '5 properties'                        |   8.874 ns |   6.2752 ns | 0.3440 ns | 0.0029 |      48 B |
| '10 properties'                       |  17.087 ns |   9.3984 ns | 0.5152 ns | 0.0048 |      80 B |
| 'Map: 1 variable'                     |  35.195 ns |  56.1062 ns | 3.0754 ns | 0.0153 |     256 B |
| 'Map: 5 variables'                    |  90.270 ns | 132.2053 ns | 7.2466 ns | 0.0300 |     504 B |
| 'Map: 10 variables'                   | 175.951 ns |  22.0808 ns | 1.2103 ns | 0.0544 |     912 B |
| 'MapTo: 1 variable'                   |  27.672 ns |   1.4650 ns | 0.0803 ns | 0.0138 |     232 B |
| 'MapTo: 5 variables'                  | 102.566 ns |  81.4480 ns | 4.4644 ns | 0.0286 |     480 B |
| 'MapTo: 10 variables'                 | 159.800 ns |  41.5725 ns | 2.2787 ns | 0.0525 |     880 B |
| 'MapTo: 5 properties'                 |   4.452 ns |   0.7323 ns | 0.0401 ns |      - |         - |
| 'MapTo: 10 properties'                |   7.018 ns |   7.0556 ns | 0.3867 ns |      - |         - |
| 'MapTo: List<T> (1 item)'             |  37.802 ns |  29.7460 ns | 1.6305 ns | 0.0095 |     160 B |
| 'MapTo: 5-level nesting'              |  23.557 ns |  28.6322 ns | 1.5694 ns | 0.0072 |     120 B |
| 'Nested (empty objects)'              |   8.305 ns |   7.1202 ns | 0.3903 ns | 0.0029 |      48 B |
| '5-level nesting'                     |  25.058 ns |  44.0275 ns | 2.4133 ns | 0.0091 |     152 B |
| '10-level nesting'                    |  46.549 ns |   9.0525 ns | 0.4962 ns | 0.0186 |     312 B |
| 'List<T> (1 item)'                    |  57.999 ns |  73.6764 ns | 4.0385 ns | 0.0110 |     184 B |
| 'T[] (1 item)'                        |  48.947 ns |  12.7049 ns | 0.6964 ns | 0.0076 |     128 B |
| 'HashSet<T> (1 item)'                 | 164.377 ns |  46.0255 ns | 2.5228 ns | 0.0191 |     320 B |
| 'Dictionary<K,V> (1 item)'            |  45.459 ns |  32.3739 ns | 1.7745 ns | 0.0157 |     264 B |
| 'Baseline: new List (1 item)'         |  14.965 ns |   6.9183 ns | 0.3792 ns | 0.0052 |      88 B |
| 'Baseline: Select+ToList (1 item)'    |  32.091 ns |  48.4177 ns | 2.6539 ns | 0.0095 |     160 B |
| 'Baseline: Select+ToArray (1 item)'   |  36.390 ns |  16.9963 ns | 0.9316 ns | 0.0062 |     104 B |
| 'Baseline: Select+ToHashSet (1 item)' | 143.512 ns |  67.2331 ns | 3.6853 ns | 0.0176 |     296 B |


## Documentation

- [Specification](SPEC.md) — full design philosophy, API surface, and architectural decisions
- [Getting Started](docs/getting-started.md) — installation, first mapper, DI registration
- [Features Guide](docs/features.md) — nested mappers, optionals, variables, enums, MapTo
- [Recommendations](docs/recommendations.md) — patterns, pitfalls, and production guidance

## Requirements

- .NET 9
- C# 13

## License

Copyright (c) Tibold Kandrai. All rights reserved.
