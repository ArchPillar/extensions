# ArchPillar.Extensions.Primitives

Foundational types for the `ArchPillar.Extensions.*` family. Lightweight, allocation-conscious, AOT/trim-safe — no dependencies beyond the BCL.

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

## Status factories

```csharp
OperationResult.Ok();                                     // 200
OperationResult.Created();                                // 201
OperationResult.Accepted();                               // 202
OperationResult.NoContent();                              // 204
OperationResult.BadRequest("invalid");                    // 400 + Problem.Detail = "invalid"
OperationResult.Unauthorized();                           // 401
OperationResult.Forbidden();                              // 403
OperationResult.NotFound("entity missing");               // 404 + Problem.Detail
OperationResult.Conflict("already exists");               // 409
OperationResult.Failed(exception);                        // 500 + Exception captured at top-level + Problem.Detail = exception.Message
OperationResult.Failure(status, type, title, detail);     // arbitrary

OperationResult<Order>.Ok(order);
OperationResult<Order>.Created(order);
OperationResult<Order>.NotFound("missing");
```

## Why an `int`-aligned enum?

The enum gives you compile-time discoverability for the common cases while leaving the door open for custom domain codes — when you really need 418, cast `(OperationStatus)418`. The numeric values match HTTP one-to-one so a status maps to an HTTP response without translation, and a result coming back from an HTTP boundary can round-trip through the enum without loss.
