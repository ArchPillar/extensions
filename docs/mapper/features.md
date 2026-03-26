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
- Collection properties are **replaced** with a newly mapped collection
- If `source` is `null`, the call is a no-op
- If `destination` is `null`, `ArgumentNullException` is thrown
- Zero allocation for scalar-only mappings (no intermediate object is created)

`MapTo` is in-memory only — there is no LINQ/EF Core equivalent.

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

### Constraints

- Transformers must return an expression of the same type as their input
- The expression body must remain a `MemberInitExpression`
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
