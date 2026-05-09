# ArchPillar.Extensions.Commands

A small, in-process command dispatcher built on `ArchPillar.Extensions.Pipelines` and `ArchPillar.Extensions.Primitives`.

## At a glance

- **Commands only.** The library dispatches `ICommand` and `ICommand<TResult>` — writes that mutate state and return an outcome. Reads and events are out of scope.
- **Single shared pipeline.** All commands flow through one `Pipeline<CommandContext>`. Cross-cutting concerns (validation, transactions, logging, idempotency) plug in as middlewares.
- **AOT/trim-safe.** No `MakeGenericMethod`, no runtime assembly scanning, no source generators required. Each `AddCommandHandler<TCommand, THandler>` call captures its generic types at the registration site.
- **Lazy router.** Handler descriptors are resolved on first dispatch of a given command type, then cached. Startup cost is proportional to commands actually used.
- **Result-first.** Every dispatch returns an `OperationResult` / `OperationResult<TResult>`. Exceptions can be raised via the implicit `throw OperationResult.NotFound(...)` form and are unwrapped by the built-in exception middleware.

See [`SPEC.md`](./SPEC.md) for the design contract, [`getting-started.md`](./getting-started.md) for a walkthrough, and [`recommendations.md`](./recommendations.md) for production patterns (Minimal-API wiring, transaction middleware, error projection, batching). A runnable Minimal-API service lives in [`samples/Commands/Command.WebApiSample/`](../../samples/Commands/Command.WebApiSample/).

## TL;DR

```csharp
public sealed record CreateOrder(string Customer, int Quantity) : ICommand<Guid>;

public sealed class CreateOrderHandler(OrderContext context) : CommandHandlerBase<CreateOrder, Guid>
{
    public override Task ValidateAsync(CreateOrder command, IValidationContext validation, CancellationToken cancellationToken)
    {
        validation.NotEmpty(command.Customer)
                  .Range(command.Quantity, 1, 100);
        return Task.CompletedTask;
    }

    public override async Task<OperationResult<Guid>> HandleAsync(CreateOrder command, CancellationToken cancellationToken)
    {
        var customer = await context.Customers.FindAsync([command.Customer], cancellationToken);
        EnsureFound(customer, "Customer not found");

        var order = new Order(customer, command.Quantity);
        context.Orders.Add(order);
        await context.SaveChangesAsync(cancellationToken);
        return Created(order.Id);
    }
}
```

```csharp
services.AddCommands();
services.AddCommandHandler<CreateOrder, Guid, CreateOrderHandler>();

// Add cross-cutting concerns via the existing Pipelines API:
services.AddPipelineMiddleware<CommandContext, TransactionMiddleware>();

// Dispatch:
OperationResult<Guid> result = await dispatcher.SendAsync(new CreateOrder("alice", 3));
```
