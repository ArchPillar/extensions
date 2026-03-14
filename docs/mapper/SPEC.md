# Mapper Library Specification

## Overview

A .NET C# library for object-to-object DTO mapping and LINQ/EF Core expression projection. Designed around **simplicity, transparency, and traceability** — a deliberate departure from convention-over-configuration approaches like AutoMapper.

The core philosophy: mappers are explicit, named, visible, and traceable. There is no magic. Every mapped property is declared. IDE navigation (Go to Definition, Find All References) works on mapper code the same as any other code.

---

## Goals

- **Traceability**: every mapper and variable is a named property on a concrete class; usages can be found via standard IDE tooling
- **Simplicity**: minimal API surface, no attribute soup, no global static state
- **Type safety**: mapping mismatches should be caught at compile time where possible
- **Dual mode**: the same mapper definition drives both in-memory object mapping and LINQ expression projection
- **Reuse / composability**: nested model mappers are automatically reused without re-declaration
- **Optional properties**: properties can be opt-in (like EF Core `.Include()`), only applied when explicitly requested
- **Runtime variables**: expression slots that can be filled with a value or `null` at invocation time, discoverable as typed C# properties
- **Performance**: expression compilation is cached; object mapping uses compiled delegates

---

## Non-Goals

- No convention-based auto-discovery of properties
- No global mapper registry
- No reflection-based mapping at runtime (except for initial compilation)
- No XML/attribute configuration

---

## Design Pattern: Mapper Context

Mappers are grouped into a **Mapper Context** class, modeled after how `DbContext` works in EF Core.

```csharp
public class AppMappers : MapperContext
{
    // Variables are real C# properties — discoverable, navigable, referenceable
    public Variable<int> CurrentUserId { get; } = CreateVariable<int>();

    public Mapper<OrderLine, OrderLineDto> OrderLine { get; }
    public Mapper<Order, OrderDto> Order { get; }

    public AppMappers()
    {
        // Property-by-property style
        OrderLine = CreateMapper<OrderLine, OrderLineDto>()
            .Map(dest => dest.ProductName, src => src.Product.Name)
            .Map(dest => dest.Quantity,    src => src.Quantity)
            .Map(dest => dest.UnitPrice,   src => src.UnitPrice);

        // Member-init expression passed directly to CreateMapper (optional)
        Order = CreateMapper<Order, OrderDto>(src => new OrderDto
            {
                Id       = src.Id,
                PlacedAt = src.CreatedAt,
                Lines    = OrderLine.Map(src.Lines),   // nested mapper reuse
                IsOwner  = src.OwnerId == CurrentUserId,
            })
            .Optional(dest => dest.CustomerName, src => src.Customer.Name); // opt-in
    }
}
```

---

## Core Concepts

### 1. MapperContext

Abstract base class that hosts mapper and variable properties.

- Users subclass `MapperContext`
- Mapper and variable properties are initialized in the constructor
- Provides `CreateMapper<TSource, TDest>()` and `CreateVariable<T>(name?, defaultValue?)` factory methods

**Logical grouping via nesting**: `MapperContext` subclasses can be composed into larger contexts via constructor injection — no library support required. This is a plain C# pattern:

```csharp
public class AppMappers
{
    public OrderMappers Orders { get; }
    public ProductMappers Products { get; }

    public AppMappers(OrderMappers orders, ProductMappers products)
    {
        Orders   = orders;
        Products = products;
    }
}
```

The library imposes no constraint here — this is left entirely to the developer and their DI container.

### 2. Mapper&lt;TSource, TDest&gt;

Represents a single mapping configuration. `TDest` must have a public parameterless constructor — constructor-based mapping is not supported because EF Core cannot translate parameterized constructor calls in expression trees. This is validated at build time when `CreateMapper` is called.

- Configured via a fluent builder
- Produces both:
  - A compiled `Func<TSource, TDest>` delegate for in-memory mapping
  - A `Expression<Func<TSource, TDest>>` for LINQ/EF Core projection

### 3. Property Mapping

Two styles are available and can be combined:

**Member-init expression** (passed to `CreateMapper` as an optional parameter):

```csharp
CreateMapper<Order, OrderDto>(src => new OrderDto
{
    Id       = src.Id,
    PlacedAt = src.CreatedAt,
})
```

This expression is used as the base mapping. Additional `.Map()` or `.Optional()` calls extend it.

**Property-by-property** fluent style:

```csharp
CreateMapper<Order, OrderDto>()
    .Map(dest => dest.Id,       src => src.Id)
    .Map(dest => dest.PlacedAt, src => src.CreatedAt)
```

Both styles can be mixed: use a member-init expression for simple properties, then chain `.Optional()` for opt-in ones.

### 4. Nested Mapper Reuse

The expression visitor detects two call patterns and inlines the nested mapper's expression tree at build time:

**Single nested object** — call `Map(src.X)` (the expression-safe, no-options overload):

```csharp
Order = CreateMapper<Order, OrderDto>(src => new OrderDto
{
    Customer = CustomerMapper.Map(src.Customer),
})
```

**Nested collections** — use the `IEnumerable<T>.Project(mapper)` extension. The caller controls the target collection type:

```csharp
Order = CreateMapper<Order, OrderDto>(src => new OrderDto
{
    Lines = src.Lines.Project(OrderLine).ToList(),
    Tags  = src.Tags.Project(TagMapper).ToHashSet(),
})
```

**Dictionaries** — use `Map(src.X)` inside standard LINQ `ToDictionary` lambdas:

```csharp
Order = CreateMapper<Order, OrderDto>(src => new OrderDto
{
    LookupById = src.Items.ToDictionary(i => i.Id, i => ItemMapper.Map(i)),
})
```

In all cases the visitor inlines the nested mapper's full `MemberInitExpression` — the LINQ provider sees no delegate calls, only a plain expression tree, ensuring full server-side translation in EF Core (no N+1, no client-side evaluation). `OrderLine`, `TagMapper`, `ItemMapper` are all real C# property references, fully navigable by IDE tooling.

### 5. Optional Properties

Properties declared as optional are excluded from the default mapping and must be explicitly requested at the call site:

```csharp
CreateMapper<Order, OrderDto>()
    .Optional(dest => dest.CustomerName, src => src.Customer.Name)
```

Requesting optional properties — including on deeply nested types and types inside collections — mirrors EF Core's `Include` / `ThenInclude` chaining. Both a typed lambda form and a dot-separated string form are supported. The string form enables optional property sets to be received from HTTP API parameters or other external sources.

```csharp
// Typed lambda — compile-time safe, IDE-navigable
query.Project(mapper.Order, o => o
    .Include(m => m.CustomerName)
    .Include(m => m.Lines, line => line.Include(l => l.SupplierName)));

// String notation — same semantics, useful when driven by external input
query.Project(mapper.Order, o => o
    .Include("CustomerName")
    .Include("Lines.SupplierName"));

// Both forms can be combined freely
query.Project(mapper.Order, o => o
    .Include(m => m.CustomerName)
    .Include("Lines.SupplierName"));

// Object mapping — variables only (MapOptions has Set but not Include)
mapper.Order.Map(order, o => o
    .Set(mapper.CurrentUserId, userId));
```

String paths are validated at call time against the mapper's declared optional properties; an unknown path throws an `InvalidOperationException` with the offending path in the message.

The options builder is aware of the nested mapper's declared optional properties and provides them in a typed, chained manner.

### 6. Runtime Variables

Variables are typed, named properties on the `MapperContext`. They act as expression placeholders that are substituted with actual values (or `null` / `default`) at call time.

```csharp
// Declared on the context — a real C# property, fully navigable
public Variable<int> CurrentUserId { get; } = CreateVariable<int>();

// Used inside a mapping expression
Order = CreateMapper<Order, OrderDto>(src => new OrderDto
{
    IsOwner = src.OwnerId == CurrentUserId,
})
```

At the call site, the caller references the context property — no magic strings:

```csharp
query.Project(mapper.Order, o => o.Set(mapper.CurrentUserId, userId));
```

Because `CurrentUserId` is a property, Go to Definition navigates to its declaration, and Find All References shows every place it is set or used.

Variables that are not set at the call site resolve to `default(T)` (i.e., `0` for `int`, `null` for reference types).

### 7. Enum Mapping

Enum-to-enum mappings are defined as explicit mapping methods — not as expression lambdas. The library generates the mapping expression automatically by calling the method for every possible input value and recording the output, producing a `switch`-expression tree that EF Core can translate.

```csharp
public class AppMappers : MapperContext
{
    public EnumMapper<OrderStatus, OrderStatusDto> OrderStatus { get; }

    public AppMappers()
    {
        OrderStatus = CreateEnumMapper<OrderStatus, OrderStatusDto>(MapOrderStatus);
    }

    private static OrderStatusDto MapOrderStatus(OrderStatus status) => status switch
    {
        OrderStatus.Pending   => OrderStatusDto.Pending,
        OrderStatus.Shipped   => OrderStatusDto.Shipped,
        OrderStatus.Cancelled => OrderStatusDto.Cancelled,
        _                     => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
```

The generated expression is equivalent to:

```csharp
src => src.Status == OrderStatus.Pending   ? OrderStatusDto.Pending   :
       src.Status == OrderStatus.Shipped   ? OrderStatusDto.Shipped   :
       src.Status == OrderStatus.Cancelled ? OrderStatusDto.Cancelled :
       default(OrderStatusDto)
```

The fallback uses `default(TDest)` instead of `throw` — all enum values are covered by the conditional chain, and `default` keeps the expression translatable by EF Core.

`EnumMapper<TSource, TDest>` can be used standalone or inlined into a parent mapper just like a regular `Mapper<T,T>`.

### 8. Null Handling

All mappers treat a null source as a null destination — no exceptions. This applies to both in-memory object mapping and LINQ expression projection.

For object mapping, the compiled delegate performs an upfront null check and returns `null` immediately.

For expression projection, no explicit null check is emitted into the expression tree — EF Core and other LINQ providers already handle null propagation correctly at the SQL level.

### 9. MapTo — Mapping onto an Existing Object

`MapTo` assigns mapped properties onto a **pre-existing** destination instance rather than creating a new one. This is useful for:

- Patch / update flows where an entity is already tracked by EF Core
- Updating an existing DTO or view-model in place
- Scenarios where the caller owns the destination lifetime

```csharp
// Update an existing tracked entity from a command object
mapper.Order.MapTo(command, existingOrder);

// With a runtime variable
mapper.Order.MapTo(command, existingOrder, o => o.Set(mapper.CurrentUserId, userId));
```

**Behavior**:

- All required properties are always assigned — identical to the in-memory `Map` default.
- Optional properties are **also assigned** (the same `IncludeAll` rule that governs in-memory `Map` applies).
- Variables can be provided via `MapOptions`, same as `Map`.
- Nested scalar properties are **replaced** with a newly mapped object; the existing nested object is not recursively merged.
- Collection properties are **replaced** with a newly mapped collection.
- If `source` is `null`, the call is a no-op — the destination is left unchanged.
- `destination` must not be `null`; passing `null` throws `ArgumentNullException`.

**In-memory only**: `MapTo` has no LINQ/EF Core equivalent. LINQ projections always produce new objects; merging into an existing tracked entity is a responsibility left to the application layer.

**Implementation**: builds a compiled `Action<TSource, TDest>` using a `BlockExpression` of `Expression.Assign` calls — one per mapped property — rather than the `MemberInitExpression` used by `Map`.

---

## API Surface

### MapperContext

```csharp
public abstract class MapperContext
{
    protected static MapperBuilder<TSource, TDest> CreateMapper<TSource, TDest>(
        Expression<Func<TSource, TDest>>? memberInitExpression = null);

    protected static EnumMapper<TSource, TDest> CreateEnumMapper<TSource, TDest>(
        Func<TSource, TDest> mappingMethod)
        where TSource : struct, Enum
        where TDest   : struct, Enum;

    protected static Variable<T> CreateVariable<T>(string? name = null, T? defaultValue = default);

    // Forces expression assembly and delegate compilation for every mapper
    // on this context. Call at the end of a subclass constructor to surface
    // errors at startup and eliminate cold-start latency.
    protected void EagerBuildAll();
}
```

### MapperBuilder&lt;TSource, TDest&gt;

```csharp
public sealed class MapperBuilder<TSource, TDest>
{
    public MapperBuilder<TSource, TDest> Map<TValue>(
        Expression<Func<TDest, TValue>> dest,
        Expression<Func<TSource, TValue>> src);

    public MapperBuilder<TSource, TDest> Optional<TValue>(
        Expression<Func<TDest, TValue>> dest,
        Expression<Func<TSource, TValue>> src);

    public MapperBuilder<TSource, TDest> Ignore<TValue>(
        Expression<Func<TDest, TValue>> dest);

    public Mapper<TSource, TDest> Build();

    public static implicit operator Mapper<TSource, TDest>(MapperBuilder<TSource, TDest> builder);
}
```

### Mapper&lt;TSource, TDest&gt;

```csharp
public sealed class Mapper<TSource, TDest>
{
    // Call-site mapping (optional params allowed)
    TDest? Map(TSource? source, Action<MapOptions<TDest>>? options = null);

    // Expression-safe single-item overload — no optional params, usable inside
    // member-init expressions and ToDictionary lambdas
    TDest? Map(TSource source);

    // Maps onto an existing destination; no-op if source is null (see §9)
    void MapTo(TSource? source, TDest destination, Action<MapOptions<TDest>>? options = null);

    // Expression tree
    Expression<Func<TSource, TDest>> ToExpression(Action<ProjectionOptions<TDest>>? options = null);
}
```

### MapperExtensions

```csharp
public static class MapperExtensions
{
    // IQueryable<T> — server-side projection (EF Core)
    public static IQueryable<TDest> Project<TSource, TDest>(
        this IQueryable<TSource> query,
        Mapper<TSource, TDest> mapper,
        Action<ProjectionOptions<TDest>>? options = null);

    // IEnumerable<T> — expression-safe (no optional params), for use inside
    // member-init expressions; caller chooses output collection via ToList() etc.
    public static IEnumerable<TDest> Project<TSource, TDest>(
        this IEnumerable<TSource> source,
        Mapper<TSource, TDest> mapper);

    // IEnumerable<T> — standalone use with options
    public static IEnumerable<TDest> Project<TSource, TDest>(
        this IEnumerable<TSource> source,
        Mapper<TSource, TDest> mapper,
        Action<MapOptions<TDest>> options);
}
```

Call site examples:

```csharp
// EF Core query projection
var results = await dbContext.Orders
    .Where(o => o.IsActive)
    .Project(mapper.Order, o => o
        .Include(m => m.CustomerName)
        .Set(mapper.CurrentUserId, currentUser.Id))
    .ToListAsync();

// In-memory collection projection
var dtos = orders.Project(mapper.Order).ToList();

// Inside a parent mapper member-init expression
Order = CreateMapper<Order, OrderDto>(src => new OrderDto
{
    Lines      = src.Lines.Project(OrderLine).ToList(),
    Tags       = src.Tags.Project(TagMapper).ToHashSet(),
    Customer   = CustomerMapper.Map(src.Customer),                   // single object
    LookupById = src.Items.ToDictionary(i => i.Id, i => Item.Map(i)), // dictionary
});
```

### MapOptions / ProjectionOptions

```csharp
public sealed class MapOptions<TDest>
{
    public MapOptions<TDest> Set<T>(Variable<T> variable, T value);
}

public sealed class ProjectionOptions<TDest>
{
    public ProjectionOptions<TDest> Include<TValue>(Expression<Func<TDest, TValue>> optionalProp);
    public ProjectionOptions<TDest> Include(string path);
    public ProjectionOptions<TDest> Set<T>(Variable<T> variable, T value);
}
```

### Variable&lt;T&gt;

```csharp
public sealed class Variable<T>
{
    // Implicitly usable inside expressions — the library replaces it at projection time
    public static implicit operator T(Variable<T> variable);
}
```

The implicit operator allows `Variable<int>` to appear directly in expression bodies (e.g., `src.OwnerId == CurrentUserId`) without casting. The `ExpressionVisitor` replaces occurrences of the variable's node with a `ConstantExpression` at call time.

---

## Expression Building

For LINQ projection the library constructs or adapts a `MemberInitExpression`:

```
src => new TDest
{
    Prop1 = <inline src expression>,
    Prop2 = <inline nested mapper MemberInitExpression>,
    ...
    OptionalProp = <only present when requested>
}
```

The pipeline applied before handing the expression to the LINQ provider:

1. **Nested mapper inlining** — `ExpressionVisitor` finds `Mapper<T,T>.Map()` calls and replaces them with the nested mapper's full expression tree (parameter-substituted).
2. **Optional property injection** — requested optional `MemberBinding`s are appended to the `MemberInitExpression`.
3. **Variable substitution** — `Variable<T>` nodes are replaced with `ConstantExpression` for the provided value, or `Expression.Default(typeof(T))` if not set.

---

## Error Handling & Validation

- Destination types must have a public parameterless constructor — validated at build time when `CreateMapper` is called. Constructor-based mapping (e.g., `src => new TDest(src.Id, src.Name)`) is not supported because EF Core cannot translate parameterized constructor calls in expression trees. If the destination type lacks a parameterless constructor or the member-init expression uses a parameterized constructor, an `InvalidOperationException` is thrown.
- Every destination property must appear in exactly one of: a member-init expression, a `.Map()` call, an `.Optional()` call, or an `.Ignore()` call. The API is designed so that **an unmapped destination property is not a reachable state** — the builder tracks coverage and throws at the point `.Build()` is called (implicit or explicit) if any property is unaccounted for.
- Attempting to inline a nested mapper that has not been built yet throws a clear exception identifying the property.
- Null safety: see §8.

---

## Traceability

Every artifact is a named C# symbol:

| Artifact | How to find usages |
| --- | --- |
| `mapper.Order` | Find All References on the property |
| `mapper.CurrentUserId` | Find All References on the property |
| Optional property `CustomerName` | Find All References on the lambda parameter |

No magic strings. No global registry. No `Map<OrderDto>(source)` where the mapper is selected invisibly.

---

## Project Structure

```
Mapper/
  src/
    Mapper/               # core library (abstractions + expression engine)
  tests/
    Mapper.Tests/         # unit + integration tests (EF Core in-memory provider)
  benchmarks/
    Mapper.Benchmarks/    # BenchmarkDotNet benchmarks (run with dotnet run -c Release)
  samples/
    Mapper.Samples/       # usage examples
```

---

## Target Frameworks

- .NET 9
- C# 13

---

## Dependencies

- `System.Linq.Expressions` (built-in)
- Test project: `xUnit`, `Microsoft.EntityFrameworkCore.InMemory`

---

## Decisions

| Topic | Decision |
| --- | --- |
| **Eager vs lazy build** | Lazy — mappers compile on first use. Startup time is a priority; there is no upfront validation cost unless mappers are exercised. |
| **Ignore API** | Explicit — `.Ignore(dest => dest.Prop)` must be called for any destination property that is intentionally left unmapped. Silence is not acceptance. |
| **Collection handling** | Transparent — `IEnumerable<T>`, `List<T>`, and `ICollection<T>` are all handled without extra configuration. |
| **Optional on nested / collection types** | Supported via `ThenInclude`-style chaining (see §5). |
| **MapTo / merge** | In-memory only — maps onto an existing destination via a compiled `Action<TSource, TDest>`. No LINQ equivalent; merging into tracked EF Core entities is an application-layer concern. Nested objects are replaced, not recursively merged. |
| **Reverse mapping** | Deferred — only considered if it does not add meaningful complexity. Not a v1 requirement. |
| **Source generators** | On the roadmap — a Roslyn source generator that emits mapping delegates at compile time for zero-allocation object mapping is a target for a future milestone. |
| **Enum mapping** | Special-cased — defined as a plain method; the library generates a switch expression tree by enumerating all enum values (see §7). |
| **Null inputs** | All mappers return `null` for a null source. No configuration needed (see §8). |
