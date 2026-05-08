# Getting started with ArchPillar.Extensions.Commands

This walkthrough builds a small command-driven module from scratch — declaring commands, writing handlers with validation, registering everything with DI, and observing the result.

## 1. Install

```xml
<PackageReference Include="ArchPillar.Extensions.Commands" Version="*" />
```

`ArchPillar.Extensions.Pipelines` and `ArchPillar.Extensions.Primitives` are pulled in as dependencies.

## 2. Declare commands

```csharp
using ArchPillar.Extensions.Commands;

public sealed record CreateOrder(string Customer, int Quantity) : ICommand<Guid>;
public sealed record CancelOrder(Guid OrderId)                  : ICommand;
```

`ICommand` when there's no payload to return (status only); `ICommand<TResult>` for commands that return one.

## 3. Write handlers

The optional base classes ship status factories (`Ok`, `Created`, `NotFound`, …) and assert helpers (`EnsureFound`, `Ensure`, `EnsureAuthorized`).

```csharp
using ArchPillar.Extensions.Commands;
using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Operations;

public sealed class CreateOrderHandler(OrderContext context)
    : CommandHandlerBase<CreateOrder, Guid>
{
    public override Task ValidateAsync(CreateOrder command, IValidationContext validation, CancellationToken cancellationToken)
    {
        // Field name is auto-captured from the argument expression — pass it
        // explicitly only if you want a friendlier label than "command.Customer".
        validation.NotEmpty(command.Customer)
                  .Range(command.Quantity, 1, 100);
        return Task.CompletedTask;
    }

    public override async Task<OperationResult<Guid>> HandleAsync(CreateOrder command, CancellationToken cancellationToken)
    {
        var customer = await context.Customers.FindAsync([command.Customer], cancellationToken);
        EnsureFound(customer, "Customer not found.");

        var order = new Order(customer, command.Quantity);
        context.Orders.Add(order);
        await context.SaveChangesAsync(cancellationToken);
        return Created(order.Id);       // helper returns OperationResult<Guid>
    }
}

// Failure helpers return OperationFailure. Either return them directly
// (the implicit conversion lifts to the typed result) or throw them:
public override async Task<OperationResult<Guid>> HandleAsync(CreateOrder command, CancellationToken cancellationToken)
{
    if (await context.Orders.AnyAsync(o => o.Customer == command.Customer, cancellationToken))
        return Conflict("Order already exists.");                    // OperationFailure → OperationResult<Guid>

    var order = new Order(command.Customer, command.Quantity);
    context.Orders.Add(order);
    await context.SaveChangesAsync(cancellationToken);
    return Created(order.Id);
}

public sealed class CancelOrderHandler(OrderContext context) : CommandHandlerBase<CancelOrder>
{
    public override Task ValidateAsync(CancelOrder command, IValidationContext validation, CancellationToken cancellationToken)
    {
        validation.Must(command.OrderId != Guid.Empty, "required", "OrderId is required.", nameof(command.OrderId));
        // Validate persisted state alongside shape — runs in the same scope as
        // the handler's write, so any wrapping transaction is in effect.
        return Task.CompletedTask;
    }

    public override async Task<OperationResult> HandleAsync(CancelOrder command, CancellationToken cancellationToken)
    {
        var order = await context.Orders.FindAsync([command.OrderId], cancellationToken);
        EnsureFound(order, "Order missing.");

        order.Cancel();
        await context.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
```

A few patterns to notice:

- `Task.CompletedTask` — `ValidateAsync` returns it from a synchronous body. With the implicit conversion on `OperationResult`, `HandleAsync` can do the same:

  ```csharp
  public override Task<OperationResult> HandleAsync(...) => Ok();
  ```

- `EnsureFound(...)` throws an `OperationException(OperationStatus.NotFound, ...)`. The built-in `ExceptionMiddleware` catches it and writes the carried result back into the dispatch outcome — no need to write the early-return path manually.

- `validation.NotEmpty(...).Range(...)` chains; every helper returns the context so multiple checks accumulate.

## 4. Wire DI

```csharp
using Microsoft.Extensions.DependencyInjection;
using ArchPillar.Extensions.Commands;

services.AddCommands();
services.AddCommandHandler<CreateOrder, Guid, CreateOrderHandler>();
services.AddCommandHandler<CancelOrder, CancelOrderHandler>();
```

`AddCommands()` registers the dispatcher, the shared pipeline, and the two built-in middlewares (telemetry, exception). Validation runs inside the router, so any user-added middleware you register after `AddCommands()` (transactions, unit-of-work, retry, locks) wraps both validation and the handler.

## 5. Dispatch

```csharp
public sealed class OrdersEndpoints(ICommandDispatcher dispatcher)
{
    public async Task<IResult> Create(CreateOrder command)
    {
        OperationResult<Guid> result = await dispatcher.SendAsync(command);
        return result.IsSuccess
            ? Results.Created($"/orders/{result.Value}", null)
            : Results.Json(result.Problem, statusCode: (int)result.Status);
    }

    public async Task<IResult> Cancel(Guid id)
    {
        OperationResult result = await dispatcher.SendAsync(new CancelOrder(id));
        return result.IsSuccess
            ? Results.NoContent()
            : Results.Json(result.Problem, statusCode: (int)result.Status);
    }
}
```

> The snippet above takes a `CreateOrder` directly to keep the walkthrough short. In production code, keep commands internal and map them from a wire-shape request DTO at the API boundary — see [`recommendations.md`](./recommendations.md) for the layered pattern.

## 6. Add a cross-cutting concern

Cross-cutting concerns are middlewares on the shared `Pipeline<CommandContext>` — there's no second mechanism to learn:

```csharp
public sealed class TransactionMiddleware(OrderContext orderContext) : IPipelineMiddleware<CommandContext>
{
    public async Task InvokeAsync(CommandContext context, PipelineDelegate<CommandContext> next, CancellationToken cancellationToken)
    {
        await using var transaction = await orderContext.Database.BeginTransactionAsync(cancellationToken);
        await next(context, cancellationToken);
        if (context.Result is { IsSuccess: true })
            await transaction.CommitAsync(cancellationToken);
        else
            await transaction.RollbackAsync(cancellationToken);
    }
}

services.AddPipelineMiddleware<CommandContext, TransactionMiddleware>();
```

## 7. Optional batch handler

When you want bulk inserts to actually be one round-trip, opt into batching:

```csharp
public sealed class CreateOrderBatchHandler(OrderContext context) : IBatchCommandHandler<CreateOrder, Guid>
{
    public async Task<IReadOnlyList<OperationResult<Guid>>> HandleBatchAsync(
        IReadOnlyList<CreateOrder> commands, CancellationToken cancellationToken)
    {
        var orders = commands.Select(c => new Order(c.Customer, c.Quantity)).ToArray();
        context.Orders.AddRange(orders);
        await context.SaveChangesAsync(cancellationToken);
        return orders.Select(o => OperationResult.Created(o.Id)).ToArray();
    }
}

services.AddBatchCommandHandler<CreateOrder, Guid, CreateOrderBatchHandler>();
```

`SendBatchAsync` is **all-or-nothing at the validation gate**: it validates every command first, and if any one fails the whole batch is rejected without invoking the handler. When validation passes for every command, the handler receives the full input list; results come back in input order.

The batch runs through the pipeline as a **single dispatch** — wrapping middleware (transactions, retry, telemetry) sees one outer pass covering the whole group, so a transaction middleware commits or rolls back the entire batch atomically. Without a registered batch handler, `SendBatchAsync` instead loops `SendAsync` per command, giving each item its own pipeline pass and its own outcome.

## 8. Observe with OpenTelemetry

```csharp
builder.Services.AddOpenTelemetry().WithTracing(b => b
    .AddSource(CommandActivitySource.Name)
    .AddOtlpExporter());
```

Every dispatch becomes an `Activity` named `Commands.<CommandTypeName>` with a `command.type` tag.

## 9. Validate handlers at startup (optional)

Off by default to keep startup fast. Turn it on when you want missing-handler errors to surface up front rather than at first dispatch:

```csharp
using var host = builder.Build();
host.Services.ValidateCommandRegistrations();   // throws on the first handler that can't resolve
await host.RunAsync();
```

The extension creates a scope and resolves every registered handler from DI, throwing `InvalidOperationException` with the offending command type's full name on the first failure. If every handler resolves cleanly, the call returns and the host continues to start.
