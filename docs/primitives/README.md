# ArchPillar.Extensions.Primitives

Foundational types for the `ArchPillar.Extensions.*` family. Lightweight, allocation-conscious, AOT/trim-safe — no dependencies beyond the BCL.

## Public surface

| Type | Purpose |
| --- | --- |
| `OperationStatus` | Typed enum with HTTP-aligned numeric values (`Ok = 200`, `NotFound = 404`, …). Cast to `int` for HTTP code; cast back from any custom code. |
| `OperationResult` | Outcome of an operation. Carries `Status`, `Errors`, optional `Exception`. |
| `OperationResult<TValue>` | Outcome that carries a `Value` payload on success. |
| `OperationError` | `record` with `Code`, `Message`, optional `Field` and structured `Details`. |
| `OperationException` | Exception that carries an `OperationResult`. Used internally to surface results through `throw`. |

## Implicit conversions

`OperationResult` provides three implicit conversions to remove boilerplate:

```csharp
// (1) OperationResult -> Task<OperationResult>
public Task<OperationResult> HandleAsync(...) => OperationResult.Ok();   // no Task.FromResult needed

// (2) OperationResult -> Exception (becomes an OperationException)
if (entity is null) throw OperationResult.NotFound("missing");

// (3) TValue -> OperationResult<TValue>
public Task<OperationResult<Order>> HandleAsync(...)
{
    var order = ...;
    return order;  // wrapped as OperationResult<Order>.Ok(order)
}
```

The exception conversion is what makes `throw result;` work — C# applies the implicit conversion to produce an `Exception` (specifically an `OperationException`) before the throw machinery takes over.

## Status factories

`OperationResult` and `OperationResult<TValue>` ship factory methods for the common HTTP-aligned outcomes:

```csharp
OperationResult.Ok();                                     // 200
OperationResult.Created();                                // 201
OperationResult.Accepted();                               // 202
OperationResult.NoContent();                              // 204
OperationResult.BadRequest("invalid");                    // 400
OperationResult.Unauthorized();                           // 401
OperationResult.Forbidden();                              // 403
OperationResult.NotFound("entity missing");               // 404
OperationResult.Conflict("already exists");               // 409
OperationResult.ValidationFailed(errors);                 // 422
OperationResult.Failed(exception);                        // 500 with carried exception
OperationResult.Failed(OperationStatus.InternalServerError, "boom");

OperationResult<Order>.Ok(order);
OperationResult<Order>.Created(order);
OperationResult<Order>.NotFound("missing");
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
// Sync — instance:
Order order = result.Unwrap();
result.Unwrap();                                              // void on the non-generic — asserts success

// Async — extension on Task<OperationResult<T>> / Task<OperationResult>:
var order = await dispatcher.SendAsync(getOrder).UnwrapAsync();
await       dispatcher.SendAsync(cancelOrder).UnwrapAsync();
```

The async form removes the `(await …).Unwrap()` parenthesis dance. Failure becomes an `OperationException` carrying the original result — catch it if you want status-aware handling, or let it propagate to the surrounding handler.

## Why an `int`-aligned enum?

The enum gives you compile-time discoverability for the common cases while leaving the door open for custom domain codes — when you really need 418, cast `(OperationStatus)418`. The numeric values match HTTP one-to-one so a status maps to an HTTP response without translation, and a result coming back from an HTTP boundary can round-trip through the enum without loss.
