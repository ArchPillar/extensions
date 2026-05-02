# ArchPillar.Extensions.Primitives

Foundational types for the `ArchPillar.Extensions.*` family. Lightweight, allocation-conscious, AOT/trim-safe — no dependencies beyond the BCL.

## Why?

Application code that does work — write commands, domain operations, integration calls — needs a uniform way to report outcomes back to callers. Returning `Task<bool>` loses information; returning `Task<TValue>` and throwing on failure forces every layer to understand the exception hierarchy. `OperationResult` provides the third option: a small value type that carries a status, optional payload, errors, and the original exception.

## What you get

- **`OperationStatus`** — a typed enum with HTTP-aligned numeric values (200, 404, 422, 500…). Cast to `int` to get the HTTP code; cast back from any custom code.
- **`OperationResult`** / **`OperationResult<TValue>`** — the result types. Status, errors, optional exception, and on the generic variant, the payload.
- **`OperationError`** — a `record` with `Code`, `Message`, optional `Field` and structured `Details`.
- **`OperationException`** — used internally to surface results through `throw`.

## Quick start

```csharp
public Task<OperationResult<Order>> GetOrderAsync(Guid id)
{
    var order = repository.Find(id);
    if (order is null) return OperationResult<Order>.NotFound("Order missing");
    return OperationResult<Order>.Ok(order);   // implicit Task wrap — no Task.FromResult
}
```

Throwing a result short-circuits anywhere:

```csharp
if (!authorized) throw OperationResult.Forbidden("not your order");
```

Unwrap at the consumption site to get the value back, throwing on failure:

```csharp
// Instance — sync:
Order order = result.Unwrap();

// Extension — async, no parens:
var order = await dispatcher.SendAsync(query).UnwrapAsync();
```

`Unwrap()` keeps the result-first contract everywhere else but gives you a one-call shortcut at the boundary where you actually need the typed value.

The implicit conversion from `OperationResult` to `Exception` produces an `OperationException` carrying the result, which downstream code (e.g. `ArchPillar.Extensions.Commands`) unwraps back into the result.

## License

MIT — see the bundled `LICENSE` file.
