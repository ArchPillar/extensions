# Features

Every feature of `ArchPillar.Extensions.Primitives`, one section each, ordered
most-common to most-advanced. The operation-result types live under the
`ArchPillar.Extensions.Operations` namespace.

## Success factories

Construct a successful result through a static factory on `OperationResult`. The value-bearing
overloads are generic on the method, so `TValue` is inferred from the argument — you never
write `OperationResult<Order>.Ok(...)`.

```csharp
OperationResult.Ok();                  // 200, no value
OperationResult.Created();             // 201
OperationResult.Accepted();            // 202
OperationResult.NoContent();           // 204

OperationResult.Ok(order);             // OperationResult<Order>
OperationResult.Created(orderId);      // OperationResult<Guid>
OperationResult.Accepted(receipt);     // OperationResult<Receipt>
```

On success `Problem` is `null` — a successful result allocates no error body.

## Failure factories

Each failure factory returns an `OperationFailure`, which implicitly converts to any
`OperationResult<TValue>` — so the same call works regardless of the target's value type. Every
factory takes a required `detail`; optional named arguments map onto the RFC 7807 problem
fields.

```csharp
OperationResult.BadRequest("Validation failed.",
    errors: new Dictionary<string, IReadOnlyList<OperationError>> { /* … */ });   // 400, field errors

OperationResult.Unauthorized("Authentication required.");                          // 401
OperationResult.Forbidden("User lacks 'orders.cancel'.");                          // 403
OperationResult.NotFound("Order 'abc-123' missing.");                              // 404
OperationResult.Conflict("Order locked.",
    extensions: new Dictionary<string, object?> { ["lockedBy"] = "alice" });       // 409, structured extras

OperationResult.Failed(exception);                                                 // 500, captures Exception
OperationResult.Failure(status, type, title, detail);                              // escape hatch, any status
```

| Argument | Where it lands |
| --- | --- |
| `type` | `Problem.Type` (overrides the default identifier) |
| `errors` | `Problem.Errors` (only on `BadRequest`, `Conflict`, `Failure`) |
| `extensions` | `Problem.Extensions` |
| `instance` | `Problem.Instance` |

`Failed(exception)` captures the exception into the internal-only `Exception` and uses its
`Message` as `Detail`. `Failure(status, type, title, detail, …)` is the escape hatch when no
purpose-built helper fits — including for a status outside the named enum, via a cast.

## OperationStatus

`OperationStatus` is an `int`-backed enum whose members carry HTTP status codes verbatim, so a
result maps to an HTTP response without translation and round-trips back from a boundary
without loss.

```csharp
int httpCode = (int)result.Status;            // 404
var status   = (OperationStatus)418;          // custom domain code — casting is supported
```

> The named set is deliberately narrow — the codes commonly returned from command handlers.
> When you genuinely need 418, cast `(OperationStatus)418`; success is defined numerically
> (`[200, 300)`), so a custom 2xx still counts as success.

Two extension methods give the canonical RFC 7807 metadata for a status, and are the defaults
the failure factories apply:

```csharp
OperationStatus.NotFound.Type();    // "not_found"
OperationStatus.NotFound.Title();   // "Not Found"
```

## RFC 7807 problem / error shape and wire format

A failed result carries an `OperationProblem` — modelled on RFC 7807
`application/problem+json`. Top-level failures describe themselves with `Title` / `Detail`
plus optional `Extensions`; field-level failures aggregate into `Errors`, keyed by field name,
each value a list of `OperationError`.

```csharp
public sealed class OperationProblem
{
    public string? Type { get; init; }
    public string? Title { get; init; }
    public string? Detail { get; init; }
    public string? Instance { get; init; }
    public IReadOnlyDictionary<string, IReadOnlyList<OperationError>>? Errors { get; init; }
    public IReadOnlyDictionary<string, object?>? Extensions { get; init; }
}

public sealed record OperationError(
    string Type,
    string Detail,
    OperationStatus Status,
    IReadOnlyDictionary<string, object?>? Extensions = null);
```

Because every property name matches the RFC, an `OperationResult` JSON-serializes directly
into a problem-details payload that can be returned from an HTTP endpoint:

```jsonc
// Validation failure with multiple field errors
{
    "status": 400,
    "problem": {
        "type":   "validation",
        "title":  "Bad Request",
        "detail": "One or more validation errors occurred.",
        "errors": {
            "command.Quantity": [
                {
                    "type":   "out_of_range",
                    "detail": "command.Quantity must be between 1 and 100.",
                    "status": 400,
                    "extensions": { "min": 1, "max": 100, "actual": 150 }
                }
            ],
            "command.Customer": [
                { "type": "required", "detail": "command.Customer is required.", "status": 400 }
            ]
        }
    }
}

// Success
{
    "status": 200,
    "problem": null,
    "value": { }       // present on OperationResult<TValue>
}
```

> `Exception` is `[JsonIgnore]` — it is a diagnostic field, never part of the wire contract.

## Implicit conversions

Three return shapes are made terse by implicit conversions on the result types:

```csharp
// (1) OperationResult / OperationResult<TValue> -> Task<…> (Task.FromResult)
public Task<OperationResult> HandleAsync() => OperationResult.Ok();

// (2) OperationResult -> Exception (wraps in OperationException)
if (entity is null) throw OperationResult.NotFound("missing");

// (3) TValue -> OperationResult<TValue> (a successful Ok)
public OperationResult<Order> Get() { Order order = Load(); return order; }

// (4) OperationFailure -> OperationResult<TValue> (lift a failure to any value type)
public OperationResult<Order> Get(Guid id)
    => repository.Find(id) is { } order ? OperationResult.Ok(order) : OperationResult.NotFound("missing");
```

Conversion (4) is the keystone: it is why a failure factory can be returned from any handler
signature without repeating `TValue`. See [recommendations](recommendations.md) for the cases
where these conversions surprise you.

## Unwrap and UnwrapAsync

`Unwrap` is the consumption boundary where you trade a result for its value — throwing
`OperationException` on failure. The sync form is an instance method; the async form is an
extension on the result-returning task, which removes the `(await …).Unwrap()` parenthesis
dance.

```csharp
// Sync
Order order = result.Unwrap();        // returns Value on success, throws on failure
nonGenericResult.Unwrap();            // void — asserts success on the non-generic result

// Async — extension on Task<OperationResult<T>> / Task<OperationResult>
Order order = await dispatcher.SendAsync(getOrder).UnwrapAsync();
await        dispatcher.SendAsync(cancelOrder).UnwrapAsync();
```

The thrown `OperationException` carries the original result — catch it for status-aware
handling, or let it propagate to a boundary handler that maps it back to a response.

## ThrowIfFailed

`ThrowIfFailed()` throws an `OperationException` carrying the result when it is a failure, and
otherwise returns the result for chaining. It is the building block `Unwrap` is implemented
on, and is useful when you want to assert success mid-pipeline without consuming the value.

```csharp
OperationResult result = service.Reserve(orderId).ThrowIfFailed();
// continues only on success; `result` is the same instance, IsSuccess == true
```

## EF Core integration (`ArchPillar.Extensions.Primitives.EntityFrameworkCore`)

Primitives also ships strongly-typed identifiers — `Id<T>`, a `readonly struct` wrapping a
`Guid` with a phantom type `T` so an `Id<Order>` can never be assigned to an `Id<Customer>`.
The core package keeps these BCL-only and EF Core-free. The opt-in companion package
`ArchPillar.Extensions.Primitives.EntityFrameworkCore` teaches EF Core to persist them: it
registers a `ValueConverter<Id<T>, Guid>` and a `ValueComparer<Id<T>>` for every `Id<T>`
property, and a relational type-mapping plugin so `Id<T>` is a first-class SQL type inside
arbitrary LINQ queries (not just round-tripping, but `Where` / `Join` translation).

> This is an integration of Primitives, documented here as a feature — it is a separately
> published NuGet package, not a separate library. Reference it only in the project that owns
> your `DbContext`.

### Opt in once per DbContext

Add the package, then call `UseArchPillarTypedIds()` when configuring the context options. The
convention then applies to every `Id<T>` property in the model automatically.

```bash
dotnet add package ArchPillar.Extensions.Primitives.EntityFrameworkCore
```

```csharp
using ArchPillar.Extensions.Models;
using ArchPillar.Extensions.Models.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
}

public sealed class Order
{
    public Id<Order> Id { get; set; }
    public Id<Customer>? CustomerId { get; set; }   // nullable typed ids are handled too
}

// Program.cs — register on the DbContext options
builder.Services.AddDbContext<AppDbContext>(options =>
    options
        .UseNpgsql(connectionString)
        .UseArchPillarTypedIds());
```

With the convention active, `Id<T>` columns store as `Guid`/`uuid`, and typed ids translate in
queries the way the underlying `Guid` would:

```csharp
Id<Order> id = Id<Order>.New();

Order? order = await db.Orders
    .Where(o => o.Id == id)        // translated server-side; Id<Order> maps to uuid
    .FirstOrDefaultAsync();
```

### Per-property opt-in

When the global convention is not active (or you want surgical control), configure a single
property with `HasIdConversion()` in `OnModelCreating`. There is a nullable overload for
`Id<T>?` properties.

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Order>(entity =>
    {
        entity.Property(o => o.Id).HasIdConversion();
        entity.Property(o => o.CustomerId).HasIdConversion();   // nullable overload
    });
}
```

> `UseArchPillarTypedIds()` is idempotent — calling it twice on the same options builder is a
> no-op. It also has a generic `UseArchPillarTypedIds<TContext>()` overload so it chains
> alongside typed builder extensions such as `UseNpgsql<TContext>()`.
