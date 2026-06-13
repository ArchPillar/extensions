# Getting started with ArchPillar.Extensions.Primitives

A linear walkthrough from install to returning a typed result and a problem body. Each step
is a complete, copy-pasteable block.

## 1. Install

```bash
dotnet add package ArchPillar.Extensions.Primitives
```

> Targets .NET 8, 9, and 10. No dependencies beyond the BCL. The operation-result types live
> under the `ArchPillar.Extensions.Operations` namespace.

## 2. Produce a result with a factory

Return an `OperationResult<TValue>` from your application method. Use a success factory on a
happy path (`TValue` is inferred from the argument) and a failure factory otherwise — the
failure implicitly converts to `OperationResult<Order>`, so you never restate `Order`.

```csharp
using ArchPillar.Extensions.Operations;

public sealed class OrderService(IOrderRepository repository)
{
    public OperationResult<Order> GetOrder(Guid id)
    {
        Order? order = repository.Find(id);
        if (order is null)
        {
            return OperationResult.NotFound($"Order '{id}' was not found.");  // OperationFailure -> OperationResult<Order>
        }

        return OperationResult.Ok(order);                                     // TValue inferred
    }
}
```

## 3. Check IsSuccess / Unwrap the value

At the consumption boundary, branch on `IsSuccess` (or `Status`), or call `Unwrap()` to get
the value and let a failure throw an `OperationException`.

```csharp
OperationResult<Order> result = service.GetOrder(id);

if (result.IsSuccess)
{
    Order order = result.Value!;     // populated on success
    Console.WriteLine($"Loaded order {order.Id}.");
}
else
{
    Console.WriteLine($"Failed ({(int)result.Status}): {result.Problem?.Detail}");
}

// Or, when you just want the value and are happy to throw on failure:
Order unwrapped = result.Unwrap();
```

## 4. Return a failure with a problem body

Failure factories take a required `detail` and accept optional RFC 7807 fields. For
field-level validation, populate `errors`; for structured context, populate `extensions`.

```csharp
public OperationResult<Order> PlaceOrder(int quantity, string? customer)
{
    if (quantity is < 1 or > 100)
    {
        return OperationResult.BadRequest(
            "One or more validation errors occurred.",
            errors: new Dictionary<string, IReadOnlyList<OperationError>>
            {
                ["command.Quantity"] =
                [
                    new OperationError(
                        Type: "out_of_range",
                        Detail: "command.Quantity must be between 1 and 100.",
                        Status: OperationStatus.BadRequest,
                        Extensions: new Dictionary<string, object?> { ["min"] = 1, ["max"] = 100, ["actual"] = quantity }),
                ],
            });
    }

    if (string.IsNullOrWhiteSpace(customer))
    {
        return OperationResult.Conflict(
            "Order is locked by another session.",
            extensions: new Dictionary<string, object?> { ["lockedBy"] = "alice" });
    }

    return OperationResult.Created(new Order());
}
```

The resulting `OperationResult` JSON-serializes straight to an `application/problem+json`
payload — its property names already match RFC 7807, and `(int)Status` is the HTTP code.

## 5. Throw a result for an early exit

When threading a return through is noisy, throw the failure — the implicit conversion to
`Exception` wraps it in an `OperationException` that carries the original result.

```csharp
if (!user.HasPermission("orders.cancel"))
{
    throw OperationResult.Forbidden($"User '{user.Name}' lacks 'orders.cancel'.");
}
```

## Next steps

- [Features](features.md) — every factory, the wire shape, implicit conversions,
  `Unwrap`/`UnwrapAsync`, `ThrowIfFailed`, and the EF Core typed-id integration.
- [Recommendations](recommendations.md) — production patterns and the implicit-conversion
  gotchas worth knowing before you lean on them.
