# Recommendations

Patterns, pitfalls, and production guidance for ArchPillar.Mapper.

## Register as Singleton

Mappers are thread-safe. Compiled delegates are cached via `Lazy<T>`, so creating multiple instances wastes memory and compilation time.

```csharp
// Do this
builder.Services.AddSingleton<AppMappers>();

// Not this
builder.Services.AddTransient<AppMappers>(); // Recompiles on every request
```

## Use EagerBuildAll in Production

Lazy compilation is convenient during development, but in production you want to surface configuration errors at startup — not on the first HTTP request.

```csharp
public class AppMappers : MapperContext
{
    public AppMappers()
    {
        // ... mapper declarations ...

        EagerBuildAll(); // Always last — after all mappers are assigned
    }
}
```

`EagerBuildAll()` must be called after all mapper properties are assigned. It iterates over the context's public `Mapper<,>` and `EnumMapper<,>` properties and forces compilation.

## Prefer Member-Init for Simple Mappings

The member-init style is more concise and reads like a regular C# object initializer:

```csharp
// Preferred for straightforward mappings
Order = CreateMapper<Order, OrderDto>(src => new OrderDto
{
    Id       = src.Id,
    PlacedAt = src.CreatedAt,
    Status   = src.Status.ToString(),
});

// Use fluent style when you need Optional/Ignore or when the member-init
// would be awkward (e.g., complex computed expressions)
```

Use fluent `.Map()` for properties that need `.Optional()` or `.Ignore()`, or when combining with a member-init base.

## Declare Leaf Mappers First

While the library resolves nested mapper references lazily (so declaration order doesn't matter at runtime), declaring leaf mappers before their parents makes the code easier to follow:

```csharp
public AppMappers()
{
    // Level 3 (leaf)
    OrderLine = CreateMapper<OrderLine, OrderLineDto>(...);

    // Level 2 (references OrderLine)
    Order = CreateMapper<Order, OrderDto>(...);

    // Level 1 (references Order)
    User = CreateMapper<User, UserDto>(...);
}
```

## Keep Mapping Expressions EF Core-Safe

Expressions must be translatable by the LINQ provider. Avoid:

- **Method calls** that EF Core can't translate (e.g., custom C# methods, `ToString()` on complex types)
- **Delegate invocations** — use mapper references (`OrderLine.Map(...)`) instead of `Func<>` calls
- **`throw` expressions** — EF Core cannot translate them; enum mappers use `default(TDest)` as the unreachable fallback for this reason

If you need a complex transformation that can't be expressed as a LINQ-translatable expression, consider splitting it into a computed property on the source entity or a post-mapping step.

## Use Variables for Request-Scoped Data

Variables are ideal for values that change per-request but are used inside mapper expressions:

```csharp
public Variable<int> CurrentUserId { get; } = CreateVariable<int>();
public Variable<string> Locale { get; } = CreateVariable<string>(defaultValue: "en");
```

Bind them at the call site:

```csharp
mappers.Order.Map(order, o => o
    .Set(mappers.CurrentUserId, userId)
    .Set(mappers.Locale, requestLocale));
```

Unset variables resolve to `default(T)`. If this isn't desirable, provide a `defaultValue` when creating the variable.

## Use Optionals for Expensive Joins

Mark navigation properties that cause additional SQL joins as optional. This way they are only loaded when explicitly requested:

```csharp
Order = CreateMapper<Order, OrderDto>(src => new OrderDto
{
    Id     = src.Id,
    Status = src.Status.ToString(),
    Lines  = src.Lines.Project(OrderLine).ToList(),
})
.Optional(d => d.CustomerName, s => s.Customer.Name);  // Extra join
```

List endpoints can skip the join:

```csharp
// List — no Customer join
var list = await db.Orders.Project(mappers.Order).ToListAsync();

// Detail — with Customer join
var detail = await db.Orders
    .Where(o => o.Id == id)
    .Project(mappers.Order, o => o.Include(m => m.CustomerName))
    .FirstAsync();
```

## Use Ignore Intentionally

`.Ignore()` explicitly documents that a property is intentionally unmapped. Prefer it over switching to `CoverageValidation.None`, because it keeps the safety net active for other properties:

```csharp
Order = CreateMapper<Order, OrderDto>()
    .Map(d => d.Id, s => s.Id)
    .Map(d => d.Status, s => s.Status.ToString())
    .Ignore(d => d.LegacyField);  // Clear intent: this property is not mapped
```

## Avoid Circular Mapper References

Self-referencing mappers (e.g., a `TreeNode` mapper that references itself) are detected at build time and throw `InvalidOperationException` with a clear message. If you have a recursive data structure, map it manually with a depth limit rather than through the mapper.

## Testing Mappers

Test both the in-memory and expression paths:

```csharp
[Fact]
public void Order_Map_ProducesCorrectDto()
{
    var mappers = new AppMappers();
    var order = new Order { Id = 1, Status = "Active" };

    OrderDto result = mappers.Order.Map(order);

    Assert.Equal(1, result.Id);
    Assert.Equal("Active", result.Status);
}

[Fact]
public void Order_ToExpression_ProducesValidExpression()
{
    var mappers = new AppMappers();

    var expression = mappers.Order.ToExpression();
    Func<Order, OrderDto> compiled = expression.Compile();

    OrderDto result = compiled(new Order { Id = 1, Status = "Active" });
    Assert.Equal(1, result.Id);
}
```

For EF Core projection, use the in-memory provider:

```csharp
[Fact]
public async Task Order_Project_TranslatesToQuery()
{
    using var db = CreateInMemoryDb();
    var mappers = new AppMappers();

    List<OrderDto> results = await db.Orders
        .Project(mappers.Order)
        .ToListAsync();

    Assert.NotEmpty(results);
}
```

## Performance Considerations

- **Singleton registration** avoids recompilation overhead
- **EagerBuildAll** eliminates cold-start latency on first request
- **MapTo** for scalar-only updates achieves zero allocation (no intermediate object)
- **Collection mappings** have overhead proportional to `Select + ToList/ToArray/ToHashSet` — the mapper itself adds no overhead beyond the LINQ operation
- **Variables** add a small per-call allocation for the binding list; avoid using many variables if allocation pressure matters
