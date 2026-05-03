# ArchPillar.Extensions.Primitives

Foundational types for the `ArchPillar.Extensions.*` family. Lightweight, allocation-conscious, AOT/trim-safe — no dependencies beyond the BCL.

## What you get

- **`OperationStatus`** — typed enum with HTTP-aligned numeric values (200, 404, 422, 500…).
- **`OperationResult`** / **`OperationResult<TValue>`** — `Status` plus an optional `Problem` body and an internal-only `Exception`.
- **`OperationProblem`** — RFC 7807 `application/problem+json`-shaped error body. Holds `Type`, `Title`, `Detail`, `Instance`, field-keyed `Errors`, free-form `Extensions`. Returned directly from an HTTP boundary.
- **`OperationError`** — per-error item used inside `OperationProblem.Errors`: `Type`, `Detail`, `Status`, `Extensions`.
- **`OperationException`** — exception that carries an `OperationResult`. Used to surface results through `throw`.

## Quick start

```csharp
public Task<OperationResult<Order>> GetOrderAsync(Guid id)
{
    var order = repository.Find(id);
    if (order is null) return OperationResult<Order>.NotFound("Order missing");
    return OperationResult<Order>.Ok(order);
}
```

Throwing a result short-circuits anywhere:

```csharp
if (!authorized) throw OperationResult.Forbidden("not your order");
```

Unwrap at the consumption boundary:

```csharp
// Sync
Order order = result.Unwrap();

// Async — no parens
var order = await dispatcher.SendAsync(query).UnwrapAsync();
```

## Wire shape

`OperationResult` JSON-serializes with property names that match RFC 7807, so it can be returned directly from an HTTP endpoint or further serialized to a pure problem-details payload:

```jsonc
{
    "status": 400,
    "problem": {
        "type":   "validation",
        "title":  "Bad Request",
        "detail": "One or more validation errors occurred.",
        "errors": {
            "Quantity": [{ "type": "out_of_range", "detail": "...", "status": 400, "extensions": {...} }]
        }
    }
}
```

`Exception` is `[JsonIgnore]` — diagnostic-only.

## License

MIT — see the bundled `LICENSE` file.
