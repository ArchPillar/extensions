# ArchPillar.Extensions.Primitives

Foundational types for the `ArchPillar.Extensions.*` family. Lightweight, allocation-conscious, AOT/trim-safe — no dependencies beyond the BCL.

The package's assembly base namespace is `ArchPillar.Extensions`. Types live under topic sub-namespaces; the operation-result family ships under `ArchPillar.Extensions.Operations`. Future primitive areas (typed identifiers, JSON converters, …) will get their own sub-namespaces under the same package.

## Why?

Most layers end up inventing their own "result" type and then a translation layer to turn it into an HTTP response. Primitives collapses that: an `int`-aligned `OperationStatus` carries the HTTP code verbatim, and the failure body is RFC 7807 `application/problem+json`-shaped, so a result returned from a handler becomes a response without a lookup table — and a result coming *back* from an HTTP boundary round-trips through the enum without loss. The enum gives compile-time discoverability for the common cases while leaving the door open for custom domain codes: when you really need 418, cast `(OperationStatus)418`. The types stay BCL-only, allocation-conscious (no `Problem` body on success), and AOT/trim-safe; EF Core persistence for the typed-id primitives is an opt-in companion package, [`ArchPillar.Extensions.Primitives.EntityFrameworkCore`](../../src/Primitives.EntityFrameworkCore/).

## Public surface

| Type | Purpose |
| --- | --- |
| `OperationStatus` | Typed enum with HTTP-aligned numeric values (`Ok = 200`, `NotFound = 404`, …). Cast to `int` for HTTP code; cast back from any custom code. |
| `OperationResult` | Outcome of an operation — `Status` plus optional `Problem` body (RFC 7807-shaped) and an internal-only `Exception`. |
| `OperationResult<TValue>` | Outcome that carries a `Value` payload on success. |
| `OperationProblem` | RFC 7807 `application/problem+json`-shaped error body. Holds `Type`, `Title`, `Detail`, `Instance`, field-keyed `Errors`, free-form `Extensions`. |
| `OperationError` | Per-error item used inside `OperationProblem.Errors`. RFC 7807-shaped: `Type`, `Detail`, `Status`, `Extensions`. |
| `OperationException` | Exception that carries an `OperationResult`. Used to surface results through `throw`. |

## Shape on the wire

`OperationResult` JSON-serializes with property names that match RFC 7807, so it can be returned directly from an HTTP boundary or further serialized to a pure problem-details payload. Two examples:

```jsonc
// Auth failure
{
    "status": 403,
    "problem": {
        "type":    "forbidden",
        "title":   "Forbidden",
        "detail":  "User 'alice' lacks 'orders.cancel'",
        "extensions": { "rule": "user.HasPermission(...)" }
    }
}

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
    "value": { ... }    // for OperationResult<TValue>
}
```

`Exception` is `[JsonIgnore]` — it's a diagnostic field, not part of the wire contract.

## Implicit conversions

```csharp
// (1) OperationResult -> Task<OperationResult>
public Task<OperationResult> HandleAsync(...) => OperationResult.Ok();

// (2) OperationResult -> Exception (becomes an OperationException)
if (entity is null) throw OperationResult.NotFound("missing");

// (3) TValue -> OperationResult<TValue>
public Task<OperationResult<Order>> HandleAsync(...) { var order = ...; return order; }
```

## Status checks

```csharp
result.IsSuccess          // true when (int)Status is in [200, 300)
result.IsFailure          // !IsSuccess
result.ThrowIfFailed();   // throws OperationException carrying the result; returns result for chaining
```

## Unwrap

When you reach the boundary where you actually want the typed value, `Unwrap` is a one-call shortcut that throws on failure. Sync as an instance method, async as an extension on the task itself:

```csharp
// Sync
Order order = result.Unwrap();
result.Unwrap();                                              // void on the non-generic — asserts success

// Async — extension on Task<OperationResult<T>> / Task<OperationResult>
var order = await dispatcher.SendAsync(getOrder).UnwrapAsync();
await       dispatcher.SendAsync(cancelOrder).UnwrapAsync();
```

Failure becomes an `OperationException` carrying the original result — catch it if you want status-aware handling, or let it propagate to the surrounding handler.

## Factories

All construction goes through `OperationResult` static factories. **Success** factories use method-level generics so `TValue` is inferred from the argument — you never write `OperationResult<Order>.Ok(...)`. **Failure** factories return `OperationFailure`, a marker subclass with an implicit conversion onto `OperationResult<TValue>`, so the same call works regardless of the target's `TValue`:

```csharp
public Task<OperationResult<Order>> HandleAsync(...)
{
    if (notFound)  return OperationResult.NotFound("Order missing.");        // OperationFailure → OperationResult<Order>
    if (locked)    return OperationResult.Conflict("Order locked.");
    return OperationResult.Ok(order);                                        // TValue inferred
}
```

**Success — non-generic, no value:**

```csharp
OperationResult.Ok();
OperationResult.Created();
OperationResult.Accepted();
OperationResult.NoContent();
```

**Success — value-bearing, generic on the method:**

```csharp
OperationResult.Ok(order);              // OperationResult<Order>
OperationResult.Created(orderId);       // OperationResult<Guid>
OperationResult.Accepted(receipt);      // OperationResult<Receipt>
```

**Failure (return `OperationFailure`, implicit-converts to `OperationResult<T>`):**

```csharp
OperationResult.BadRequest("Validation failed.",
    errors: new Dictionary<string, IReadOnlyList<OperationError>> { ... });   // 400, plus field-keyed errors

OperationResult.Unauthorized("Authentication required.");                    // 401
OperationResult.Forbidden("User lacks 'orders.cancel'.");                    // 403
OperationResult.NotFound("Order 'abc-123' missing.");                        // 404
OperationResult.Conflict("Order locked.",
    extensions: new Dictionary<string, object?> { ["lockedBy"] = "alice" }); // 409, plus structured extras

OperationResult.Failed(exception);                                            // 500, captures Exception
OperationResult.Failure(status, type, title, detail, ...);                   // escape hatch
```

Every failure factory takes a required `detail` (the per-occurrence "what's what" message). Optional named arguments mirror the RFC 7807 problem fields:

| Argument | Where it lands |
| --- | --- |
| `type` | `Problem.Type` (overrides the default identifier) |
| `errors` | `Problem.Errors` (only on `BadRequest`, `Conflict`, `Failure`) |
| `extensions` | `Problem.Extensions` |
| `instance` | `Problem.Instance` |

## Documentation

- [Getting started](getting-started.md) — install to first result, numbered.
- [Features](features.md) — every factory, the wire shape, implicit conversions, `Unwrap`/`UnwrapAsync`, `ThrowIfFailed`, and the EF Core typed-id integration.
- [Recommendations](recommendations.md) — production patterns and the implicit-conversion gotchas.
- [Specification](internals/SPEC.md) — the design contract: goals, concepts, and full API surface.

## Samples

- [Primitives.CatalogSample](../../samples/Primitives/Primitives.CatalogSample/) — Console: the core `OperationResult` story in memory — success/failure factories, `Unwrap`/`ThrowIfFailed`, and the `OperationProblem`/`OperationError` shape.
- [Primitives.WebApiSample](../../samples/Primitives/Primitives.WebApiSample/) — ASP.NET Core Minimal API: returning `OperationResult` across the HTTP boundary as `application/problem+json` with HTTP-aligned status codes (400 field errors, 404, 201/200).
- [Primitives.TypedIdsSample](../../samples/Primitives/Primitives.TypedIdsSample/) — Console + EF Core SQLite: the opt-in `Primitives.EntityFrameworkCore` add-on — `Id<T>` persisted via `UseArchPillarTypedIds()` plus a per-property `HasIdConversion()`, with operations returning `OperationResult`.
- [Primitives.BlazorSample](../../samples/Primitives/Primitives.BlazorSample/) — Blazor WebAssembly: consuming `OperationResult`/`problem+json` client-side with no backend — client-side validation producing a result, and deserializing a canned `problem+json` into `OperationProblem`.
