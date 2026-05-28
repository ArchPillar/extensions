# Features Guide

## Mapping Styles

Two styles are available and can be freely combined. Destination types must have a public parameterless constructor — constructor-based mapping is not supported.

### Member-init expression

Pass a lambda with an object initializer to `CreateMapper`. Every property assigned in the initializer is automatically tracked as a required mapping.

```csharp
OrderLine = CreateMapper<OrderLine, OrderLineDto>(src => new OrderLineDto
{
    ProductName = src.ProductName,
    Quantity    = src.Quantity,
    UnitPrice   = src.UnitPrice,
});
```

### Fluent property-by-property

Chain `.Map()` calls on the builder. Each call maps a single destination property from a source expression.

```csharp
OrderLine = CreateMapper<OrderLine, OrderLineDto>()
    .Map(d => d.ProductName, s => s.ProductName)
    .Map(d => d.Quantity,    s => s.Quantity)
    .Map(d => d.UnitPrice,   s => s.UnitPrice);
```

### Combining both

Use a member-init expression for straightforward properties, then chain `.Map()`, `.Optional()`, or `.Ignore()` for the rest:

```csharp
Order = CreateMapper<Order, OrderDto>(src => new OrderDto
{
    Id       = src.Id,
    PlacedAt = src.CreatedAt,
    Lines    = src.Lines.Project(OrderLine).ToList(),
})
.Map(d => d.Status, s => s.Status.ToString())
.Optional(d => d.CustomerName, s => s.Customer.Name);
```

## Nested Mappers

Nested mapper expressions are inlined at build time — the LINQ provider sees a single flat expression tree with no delegate calls.

### Single object

Call `Map(src.X)` on the nested mapper property. The expression visitor detects this and inlines the nested mapper's body.

```csharp
Order = CreateMapper<Order, OrderDto>(src => new OrderDto
{
    Customer = CustomerMapper.Map(src.Customer),
});
```

If `src.Customer` is `null`, the result is `null` — a null guard is emitted automatically for reference types.

### Collections

Use the `.Project(mapper)` extension followed by a collection materializer (`.ToList()`, `.ToArray()`, `.ToHashSet()`):

```csharp
Order = CreateMapper<Order, OrderDto>(src => new OrderDto
{
    Lines = src.Lines.Project(OrderLine).ToList(),
    Tags  = src.Tags.Project(TagMapper).ToHashSet(),
});
```

### Dictionaries

Use standard `ToDictionary` with an inline `Map()` call in the value selector:

```csharp
Catalog = CreateMapper<Catalog, CatalogDto>(src => new CatalogDto
{
    Items = src.Items.ToDictionary(i => i.Key, i => ItemMapper.Map(i)),
});
```

### Conditional / ternary

Multiple mapper calls in a single expression are supported:

```csharp
Flex = CreateMapper<FlexSource, FlexDest>(s => new FlexDest
{
    Part = s.UseFirst ? PartMapper.Map(s.First) : PartMapper.Map(s.Second),
});
```

### Inline initializers

Nested `new { ... }` initializers within a parent mapper expression are supported. Mapper calls inside them are inlined correctly:

```csharp
Shipment = CreateMapper<ShipmentSource, ShipmentDest>(s => new ShipmentDest
{
    Id   = s.Id,
    Pack = new PackDest
    {
        Primary   = PartMapper.Map(s.Pack.Primary),
        Secondary = PartMapper.Map(s.Pack.Secondary),
    },
});
```

## Optional Properties

Properties declared with `.Optional()` are excluded from the default mapping and must be explicitly requested.

### Declaring

```csharp
Order = CreateMapper<Order, OrderDto>(src => new OrderDto
{
    Id     = src.Id,
    Status = src.Status.ToString(),
    Lines  = src.Lines.Project(OrderLine).ToList(),
})
.Optional(d => d.CustomerName, s => s.Customer.Name);
```

### Requesting — typed lambda

```csharp
var results = await dbContext.Orders
    .Project(mappers.Order, o => o
        .Include(m => m.CustomerName))
    .ToListAsync();
```

### Requesting — string path

Useful when include sets come from API parameters or external input:

```csharp
.Project(mappers.Order, o => o
    .Include("CustomerName")
    .Include("Lines.SupplierName"))
```

String paths are validated at call time — unknown paths throw `InvalidOperationException`.

### Nested includes

For optional properties on types inside collections, chain includes like EF Core's `ThenInclude`:

```csharp
.Project(mappers.Order, o => o
    .Include(m => m.Lines, line => line
        .Include(l => l.SupplierName)
        .Include(l => l.Product, product => product
            .Include(p => p.CategoryDescription))))
```

Both typed and string forms can be mixed freely.

### In-memory mapping

For in-memory `Map()` calls, all properties (required and optional) are always included — there is no need to call `Include`. This matches the expectation that in-memory mapping produces a complete object.

## Runtime Variables

Variables are typed, named properties on the context that act as placeholders in expressions. They are substituted with actual values at call time.

### Declaring

```csharp
public class AppMappers : MapperContext
{
    public Variable<int> CurrentUserId { get; } = CreateVariable<int>();

    public Mapper<Order, OrderDto> Order { get; }

    public AppMappers()
    {
        Order = CreateMapper<Order, OrderDto>(src => new OrderDto
        {
            Id      = src.Id,
            IsOwner = src.OwnerId == CurrentUserId, // Variable reference
        });
    }
}
```

### Binding at call time

```csharp
// In-memory
OrderDto dto = mappers.Order.Map(order, o => o.Set(mappers.CurrentUserId, userId));

// EF Core projection
var results = await dbContext.Orders
    .Project(mappers.Order, o => o.Set(mappers.CurrentUserId, currentUser.Id))
    .ToListAsync();
```

Variables not bound at the call site resolve to `default(T)` (`0` for `int`, `null` for reference types).

Because `CurrentUserId` is a C# property, Go to Definition navigates to its declaration, and Find All References shows every place it is used or set.

### Default values

Variables can have a default value other than `default(T)`:

```csharp
public Variable<string> Locale { get; } = CreateVariable<string>(defaultValue: "en");
```

## Enum Mapping

Enum-to-enum mappings are defined as plain C# methods. The library generates a LINQ-translatable conditional expression by calling the method for every source value and recording the result.

### Declaring

```csharp
public class AppMappers : MapperContext
{
    public EnumMapper<OrderStatus, OrderStatusDto> OrderStatusMapper { get; }

    public AppMappers()
    {
        OrderStatusMapper = CreateEnumMapper<OrderStatus, OrderStatusDto>(s => s switch
        {
            OrderStatus.Pending   => OrderStatusDto.Pending,
            OrderStatus.Shipped   => OrderStatusDto.Shipped,
            OrderStatus.Cancelled => OrderStatusDto.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(s)),
        });
    }
}
```

### Standalone use

```csharp
OrderStatusDto mapped = mappers.OrderStatusMapper.Map(OrderStatus.Pending);
```

### Inline in a parent mapper

```csharp
Order = CreateMapper<Order, OrderDto>(src => new OrderDto
{
    Status = OrderStatusMapper.Map(src.Status),
});
```

The generated expression is a chain of conditionals that EF Core can translate to SQL — no `throw` expressions, no delegate calls.

### Nullable enums

`EnumMapper` supports nullable source values with proper null propagation:

```csharp
Order = CreateMapper<Order, OrderDto>(src => new OrderDto
{
    // Nullable → nullable: null in → null out
    Status = OrderStatusMapper.Map(src.NullableStatus),

    // Nullable → non-nullable with default: null in → default value out
    Priority = PriorityMapper.Map(src.NullablePriority, PriorityDto.Normal),

    // Non-nullable → nullable: uses C#'s implicit conversion (no special handling)
    Category = CategoryMapper.Map(src.Category),
});
```

| Call | Return | Behaviour |
|------|--------|-----------|
| `Map(TSource)` | `TDest` | Non-nullable to non-nullable |
| `Map(TSource?)` | `TDest?` | Null in → null out |
| `Map(TSource?, TDest default)` | `TDest` | Null in → default value out |

All three overloads work standalone, inlined in parent mapper expressions, and in LINQ projection (EF Core translatable).

For standalone use, the nullable expression tree is available via `ToNullableExpression()`:

```csharp
Expression<Func<OrderStatus?, OrderStatusDto?>> expr = mappers.OrderStatusMapper.ToNullableExpression();
```

## Symmetric Enum Mapping

`SymmetricEnumMapper<TLeft, TRight>` provides bidirectional (bijective) mapping between two enum types from a single forward definition. The reverse mapping is derived automatically.

### Declaring

```csharp
public class AppMappers : MapperContext
{
    public SymmetricEnumMapper<OrderStatus, OrderStatusDto> StatusMapper { get; }

    public AppMappers()
    {
        StatusMapper = CreateSymmetricEnumMapper<OrderStatus, OrderStatusDto>(s => s switch
        {
            OrderStatus.Pending   => OrderStatusDto.Pending,
            OrderStatus.Shipped   => OrderStatusDto.Shipped,
            OrderStatus.Cancelled => OrderStatusDto.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(s)),
        });
    }
}
```

### Forward and reverse mapping

```csharp
// Forward: OrderStatus → OrderStatusDto
OrderStatusDto dto = mappers.StatusMapper.Map(OrderStatus.Pending);

// Reverse: OrderStatusDto → OrderStatus
OrderStatus domain = mappers.StatusMapper.MapReverse(OrderStatusDto.Shipped);
```

### Inline in parent mappers

```csharp
// Forward direction
Order = CreateMapper<Order, OrderDto>(src => new OrderDto
{
    Status = StatusMapper.Map(src.Status),
});

// Reverse direction
OrderFromDto = CreateMapper<OrderDto, Order>(dto => new Order
{
    Status = StatusMapper.MapReverse(dto.Status),
});
```

### Nullable support

All the same nullable overloads as `EnumMapper` are available on both `Map()` and `MapReverse()`.

### Bijection validation

The mapper validates at build time that the mapping is truly one-to-one. If two source values map to the same destination value, an `InvalidOperationException` is thrown listing the conflict. For many-to-one mappings, use `EnumMapper<,>` instead.

### Expression access

```csharp
Expression<Func<OrderStatus, OrderStatusDto>>   forward  = mappers.StatusMapper.ToExpression();
Expression<Func<OrderStatus?, OrderStatusDto?>>  fwdNull  = mappers.StatusMapper.ToNullableExpression();
Expression<Func<OrderStatusDto, OrderStatus>>    reverse  = mappers.StatusMapper.ToReverseExpression();
Expression<Func<OrderStatusDto?, OrderStatus?>>  revNull  = mappers.StatusMapper.ToReverseNullableExpression();
```

## Clone Mapper

`CreateCloneMapper<T>()` creates a mapper that auto-wires every public settable property as an identity mapping (`dest.Prop = src.Prop`). This is useful for creating shallow copies of a model.

### Declaring

```csharp
public class AppMappers : MapperContext
{
    public Mapper<Order, Order> OrderClone { get; }

    public AppMappers()
    {
        OrderClone = CreateCloneMapper<Order>();
    }
}
```

### Customizing

Chain `.Ignore()` to exclude properties, or `.Map()` to override individual mappings:

```csharp
OrderClone = CreateCloneMapper<Order>()
    .Ignore(d => d.Id)                              // Don't copy the primary key
    .Map(d => d.Lines, s => s.Lines.ToList());      // Deep-copy the collection
```

### Behavior

- The clone is **shallow** — reference-type properties copy the reference, not the object
- All standard mapper features apply: coverage validation, `MapTo`, expression projection
- Works with both in-memory mapping and LINQ projection

## MapTo

`MapTo` assigns mapped properties onto a pre-existing destination instance rather than creating a new one.

### Use cases

- Updating an EF Core tracked entity from a command/DTO
- Patching an existing view-model in place
- Scenarios where the caller owns the destination lifetime

### Usage

```csharp
// Basic
mappers.Order.MapTo(command, existingOrder);

// With variables
mappers.Order.MapTo(command, existingOrder, o => o.Set(mappers.CurrentUserId, userId));
```

### Behavior

- All required and optional properties are assigned (same `IncludeAll` rule as in-memory `Map`)
- Nested scalar properties are **replaced** (not recursively merged)
- Collection properties follow their configured `CollectionMapToMode` (default: `Shallow`)
- If `source` is `null`, the call is a no-op
- If `destination` is `null`, `ArgumentNullException` is thrown
- Zero allocation for scalar-only mappings (no intermediate object is created)

`MapTo` is in-memory only — there is no LINQ/EF Core equivalent.

### Collection update modes

By default, `MapTo` replaces collection references (same as `Map`). For scenarios that require preserving the existing collection instance or individual elements, configure the mode on the builder:

| Mode | Behaviour |
|------|-----------|
| `Shallow` (default) | Replace the collection reference with a newly mapped collection. Identical to `Map` behaviour. |
| `Deep` | Preserve the collection instance: clear it and re-add newly mapped items. The reference on the destination is unchanged. Useful for observable collections or data-binding scenarios. |
| `DeepWithIdentity` | Preserve both the collection instance and individual element instances that match by key. Existing items are updated via `MapTo`, new source items are mapped and added, unmatched destination items are removed. Critical for EF Core change-tracked collections. |

**Deep** — preserve the collection instance:

```csharp
Order = CreateMapper<Order, OrderDto>(src => new OrderDto { ... })
    .MapToCollection<OrderLineDto>(dest => dest.Lines, CollectionMapToMode.Deep);
```

**DeepWithIdentity** — preserve both the collection and matching elements:

```csharp
Order = CreateMapper<Order, OrderDto>(src => new OrderDto { ... })
    .MapToCollection(
        dest => dest.Lines,          // destination collection
        src  => src.Lines,           // raw source collection (pre-projection)
        OrderLine,                   // nested mapper (for Map + MapTo)
        srcKey:  src  => src.Id,     // source element key
        destKey: dest => dest.Id);   // destination element key
```

With `DeepWithIdentity`, EF Core's change tracker sees individual entity state changes (`Modified`, `Added`, `Deleted`) rather than a wholesale collection replacement.

## Coverage Validation

The builder validates that every destination property is accounted for. Three modes are available:

### NonNullableProperties (default)

Every non-nullable property must be covered by `.Map()`, `.Optional()`, or `.Ignore()`. Nullable reference types and nullable value types (`int?`, `string?`) are auto-ignored.

```csharp
// This is the default — no configuration needed
OrderLine = CreateMapper<OrderLine, OrderLineDto>()
    .Map(d => d.ProductName, s => s.ProductName)
    .Map(d => d.Quantity, s => s.Quantity)
    .Map(d => d.UnitPrice, s => s.UnitPrice);
    // string? SupplierName is auto-ignored
```

### AllProperties

Every writable property must be explicitly covered, regardless of nullability:

```csharp
OrderLine = CreateMapper<OrderLine, OrderLineDto>()
    .SetCoverageValidation(CoverageValidation.AllProperties)
    .Map(d => d.ProductName, s => s.ProductName)
    .Map(d => d.Quantity, s => s.Quantity)
    .Map(d => d.UnitPrice, s => s.UnitPrice)
    .Map(d => d.SupplierName, s => s.SupplierName); // Must be explicit
```

### None

Skip validation entirely. Use with caution — unmapped non-nullable value types silently receive `default` values.

```csharp
OrderLine = CreateMapper<OrderLine, OrderLineDto>()
    .SetCoverageValidation(CoverageValidation.None)
    .Map(d => d.ProductName, s => s.ProductName);
    // No error for missing properties
```

### Context-level default

Override `DefaultCoverageValidation` on your context to change the default for all mappers:

```csharp
public class StrictMappers : MapperContext
{
    protected override CoverageValidation DefaultCoverageValidation
        => CoverageValidation.AllProperties;

    // All mappers in this context default to AllProperties
}
```

Individual mappers can still override via `.SetCoverageValidation()`.

## Mapper Inheritance

When mapping to a destination type hierarchy, use `Inherit()` to reuse the base mapper's property mappings instead of duplicating them.

### Declaring a base mapper

```csharp
public class DocumentMappers : MapperContext
{
    public Mapper<Document, DocumentSummaryDto> Summary { get; }
    public Mapper<Document, DocumentDetailDto> Detail { get; }
    public Mapper<Document, DocumentStatsDto> Stats { get; }

    public DocumentMappers()
    {
        Summary = CreateMapper<Document, DocumentSummaryDto>(src => new DocumentSummaryDto
        {
            Id     = src.Id,
            Title  = src.Title,
            Author = src.Author,
        })
        .Optional(dest => dest.Category, src => src.Category);

        Detail = Inherit(Summary).For<DocumentDetailDto>()
            .Map(dest => dest.Content,   src => src.Content)
            .Map(dest => dest.CreatedAt, src => src.CreatedAt)
            .Optional(dest => dest.ReviewerName, src => src.ReviewedBy.Name);

        Stats = Inherit(Summary).For<DocumentStatsDto>()
            .Map(dest => dest.ViewCount, src => src.ViewCount);
    }
}
```

`Detail` and `Stats` automatically inherit the `Id`, `Title`, `Author`, and `Category` mappings from `Summary`. Only the new properties need to be mapped.

### Derived source types

When both source and destination types are derived, use the two-type-parameter overload:

```csharp
TechnicalDetail = Inherit(Summary).For<TechnicalDocument, TechnicalDocumentDto>()
    .Map(dest => dest.Content,   src => src.Content)
    .Map(dest => dest.CreatedAt, src => src.CreatedAt)
    .Map(dest => dest.Language,  src => src.Language);
```

### Behavior

- All property mappings (required, optional, ignored) are inherited
- Coverage validation runs against the full derived destination type
- Optional properties propagate correctly — inherited optionals remain optional
- `MapTo` works identically on inherited mappers
- Nested mapper inlining and expression transformers work as expected

## Expression Transformers

Expression transformers rewrite mapper expression trees during compilation. This is useful for replacing patterns that EF Core cannot translate (e.g., custom implicit conversions, method calls) with translatable equivalents.

### Implementing a transformer

```csharp
public class MethodCallTransformer : ExpressionVisitor, IExpressionTransformer
{
    public Expression Transform(Expression expression) => Visit(expression);

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        // Replace untranslatable method calls with EF Core-safe equivalents
        return base.VisitMethodCall(node);
    }
}
```

### Registration levels

Transformers run in order: **global → per-context → per-mapper**.

```csharp
// Global — applies to all contexts (register via DI)
var globalOptions = new GlobalMapperOptions();
globalOptions.AddTransformer(new CastTransformer());
services.AddSingleton(globalOptions);

// Per-context — applies to all mappers in this context
public class AppMappers : MapperContext
{
    public AppMappers(GlobalMapperOptions globalOptions) : base(globalOptions)
    {
        AddTransformer(new MyContextTransformer());
    }
}

// Per-mapper — applies to a single mapper
Order = CreateMapper<Order, OrderDto>(...)
    .WithTransformers(new MyMapperTransformer());
```

Alternatively, use the standard `IOptions<T>` pattern. The library itself has no
dependency on `Microsoft.Extensions.Options`, but your `MapperContext` subclass
can accept `IOptions<GlobalMapperOptions>` and unwrap it:

```csharp
services.Configure<GlobalMapperOptions>(o =>
    o.AddTransformer(new CastTransformer()));
services.AddSingleton<AppMappers>();

public class AppMappers : MapperContext
{
    public AppMappers(IOptions<GlobalMapperOptions> options) : base(options.Value)
    {
        AddTransformer(new MyContextTransformer());
    }
}
```

### Built-in base classes

The library provides two abstract base classes for common transformation patterns:

**`MethodCallTransformer`** — replaces calls to a specific method with a custom expression:

```csharp
public sealed class IsPositiveTransformer : MethodCallTransformer
{
    protected override MethodInfo Method { get; }
        = typeof(Money).GetMethod(nameof(Money.IsPositive))!;

    protected override Expression Replacement(
        Expression? instance, IReadOnlyList<Expression> arguments)
        => Expression.GreaterThan(
            Expression.Property(instance!, nameof(Money.Amount)),
            Expression.Constant(0m));
}
```

`MethodCallTransformer` automatically handles:
- Generic method instantiations (matches against the open generic definition)
- Methods inherited from generic base classes (e.g., `ValueObject<Money>`)
- Methods defined on interfaces (resolves the concrete implementation via interface mapping)

**`CastTransformer<TSource, TTarget>`** — replaces cast expressions (implicit/explicit conversions) with a custom expression:

```csharp
public sealed class MoneyToAmountTransformer : CastTransformer<Money, decimal>
{
    protected override Expression Replacement(Expression operand)
        => Expression.Property(operand, nameof(Money.Amount));
}
```

The source type match uses `IsAssignableTo`, so specifying a base class or interface matches all derived/implementing types.

### Targeting a compilation path

A mapper definition compiles into two forms: an in-memory delegate (`Map`) and a LINQ projection (`ToExpression`). By default a transformer runs on **both**. Override `Target` to confine it to one path:

```csharp
public sealed class RiskScoreSqlTransformer : MethodCallTransformer
{
    // Rewrite only on the EF Core path — the real CLR method runs in memory.
    public override TransformTarget Target => TransformTarget.ExpressionOnly;

    protected override MethodInfo Method { get; }
        = typeof(Risk).GetMethod(nameof(Risk.Score))!;

    protected override Expression Replacement(
        Expression? instance, IReadOnlyList<Expression> arguments)
        => /* DbFunction / SqlFunctionExpression call EF Core can translate */;
}
```

`TransformTarget` has three values: `Both` (default), `ExpressionOnly` (LINQ projection only), and `InMemoryOnly` (in-memory delegate only). This is for operations whose **encoding** differs per engine — e.g. a domain method that runs as real CLR code in memory but maps to a SQL function in the database. The two paths must stay **semantically equivalent**; targeting changes only how a value is computed, never the result. The active path also propagates through nested-mapper inlining, so a path-targeted transformer on a nested mapper is honoured according to the path its parent is built for.

> Prefer EF Core's native `HasDbFunction` mapping when the method can be `static` (or live on the `DbContext`): the real method runs in memory and EF translates the call in SQL with no transformer at all. Use `ExpressionOnly` only when the call site cannot be made natively translatable — for example an instance method on a domain type, which EF Core cannot map to a database function. (A stored procedure cannot be composed into a projection at all; use a scalar function instead.)

### Constraints

- Transformers must return an expression of the same type as their input
- The expression body must remain a `MemberInitExpression`
- A transformer runs only on the compilation path(s) selected by its `Target`
- Violations throw `InvalidOperationException` with a clear message identifying the offending transformer

## Composing Contexts

Multiple `MapperContext` subclasses can be composed into a larger unit via plain constructor injection — no library support required:

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

Register all contexts in DI:

```csharp
builder.Services.AddSingleton<OrderMappers>();
builder.Services.AddSingleton<ProductMappers>();
builder.Services.AddSingleton<AppMappers>();
```

This is a plain C# pattern — the library imposes no constraint on how contexts are organized or composed.

## EF Core Integration

The core library is provider-agnostic — `Project(mapper)` and `ToExpression()` produce plain expression trees that EF Core translates. The companion package **`ArchPillar.Extensions.Mapper.EntityFrameworkCore`** adds extra query support, enabled by a single call:

```bash
dotnet add package ArchPillar.Extensions.Mapper.EntityFrameworkCore
```

```csharp
// Program.cs
builder.Services.AddDbContext<AppDbContext>(options =>
    options
        .UseNpgsql(connectionString)
        .UseArchPillarMapper());
```

`UseArchPillarMapper` takes no arguments. Mappers are never declared up front:
every mapper is resolved from the constant it appears as in the query, and enum
mapper tables are computed on demand the first time a given mapper is used.

### Direct mapper calls in hand-written queries

`Project(mapper)` projects a whole row. Sometimes you instead want a custom `Select` and to map only *one* property with a mapper. Without the integration, EF Core cannot translate a `Mapper.Map()` / `Project()` call sitting inside your own projection — it is a delegate call. With it, those calls are inlined into the mapper's projection expression at query-compilation time, so the whole query is still translated server-side:

```csharp
var rows = await db.Orders
    .Where(o => o.IsActive)
    .Select(o => new OrderRow
    {
        OrderId       = o.Id,                                        // your own projection
        CustomerEmail = o.Customer.Email,                           // your own projection
        Order         = mappers.Order.Map(o),                       // one property via a mapper
        Lines         = o.Lines.Project(mappers.OrderLine).ToList(), // a collection via a mapper
    })
    .ToListAsync();
```

Both scalar `Map()` and collection `Project()` are inlined; nested mappers, enum mappers, and variables inside them resolve exactly as they do for a top-level `Project(mapper)`.

### Requesting optional properties at the call site

The plain `Map(src)` / `Project(mapper)` overloads project the required properties only. To pull in optional properties, use the options overloads from the `ArchPillar.Extensions.Mapper.EntityFrameworkCore` namespace:

```csharp
using ArchPillar.Extensions.Mapper.EntityFrameworkCore;

db.Orders.Select(o => mappers.Order.Map(o, c => c.Include(m => m.CustomerName)));

db.Orders.Select(o => new OrderRow
{
    OrderId = o.Id,
    Lines   = o.Lines.Project(mappers.OrderLine, c => c.Include(m => m.SupplierName)).ToList(),
});
```

### Enum mappers → flat SQL CASE

`EnumMapper.Map()` and `SymmetricEnumMapper.Map()` / `.MapReverse()` calls used directly in a query are translated to a flat SQL `CASE … WHEN … END` with one branch per enum value, instead of the nested conditional chain the provider-agnostic path emits. This keeps SQL nesting shallow (important for providers with nesting limits) for enums with many values:

```csharp
db.Orders.Select(o => mappers.OrderStatusMapper.Map(o.Status)); // → CASE o.Status WHEN 0 THEN … END
```

### Notes

- The integration resolves mappers through the `context.Mapper` access pattern (the same way registered contexts are scanned). Mappers reached only through a non-`MapperContext` facade are not recognized.
- Variable values bound at an inline call site bake in as constants captured at compile time; prefer `Include()` for optional properties.
- `Map()` / `Project()` used inside a **mapper definition** are always inlined by the core library and do not need this package — it is only for direct calls inside your own queries.
