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

`ICommand` for fire-and-go (status only); `ICommand<TResult>` for commands that return a payload.

## 3. Write handlers

The optional base classes ship status factories (`Ok`, `Created`, `NotFound`, …) and assert helpers (`EnsureFound`, `Ensure`, `EnsureAuthorized`).

```csharp
using ArchPillar.Extensions.Commands;
using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Primitives;

public sealed class CreateOrderHandler(IOrderRepository repo)
    : CommandHandlerBase<CreateOrder, Guid>
{
    public override Task ValidateAsync(CreateOrder cmd, IValidationContext ctx, CancellationToken ct)
    {
        ctx.NotEmpty(cmd.Customer, nameof(cmd.Customer))
           .Range(cmd.Quantity, 1, 100, nameof(cmd.Quantity));
        return Task.CompletedTask;
    }

    public override async Task<OperationResult<Guid>> HandleAsync(CreateOrder cmd, CancellationToken ct)
    {
        var customer = await repo.FindAsync(cmd.Customer, ct);
        EnsureFound(customer, "Customer not found.");

        var id = await repo.CreateAsync(customer, cmd.Quantity, ct);
        return Created(id);
    }
}

public sealed class CancelOrderHandler(IOrderRepository repo) : CommandHandlerBase<CancelOrder>
{
    public override Task ValidateAsync(CancelOrder cmd, IValidationContext ctx, CancellationToken ct)
    {
        ctx.Must(cmd.OrderId != Guid.Empty, "required", "OrderId is required.", nameof(cmd.OrderId));
        return Task.CompletedTask;
    }

    public override async Task<OperationResult> HandleAsync(CancelOrder cmd, CancellationToken ct)
    {
        var order = await repo.FindOrderAsync(cmd.OrderId, ct);
        EnsureFound(order, "Order missing.");

        await repo.CancelAsync(order, ct);
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

- `ctx.NotEmpty(...).Range(...)` chains; every helper returns the context so multiple checks accumulate.

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
    public async Task<IResult> Create(CreateOrder cmd)
    {
        OperationResult<Guid> result = await dispatcher.SendAsync(cmd);
        return result.IsSuccess
            ? Results.Created($"/orders/{result.Value}", null)
            : Results.Json(new { result.Status, result.Errors }, statusCode: (int)result.Status);
    }

    public async Task<IResult> Cancel(Guid id)
    {
        OperationResult result = await dispatcher.SendAsync(new CancelOrder(id));
        return result.IsSuccess
            ? Results.NoContent()
            : Results.Json(new { result.Status, result.Errors }, statusCode: (int)result.Status);
    }
}
```

## 6. Add a cross-cutting concern

Cross-cutting concerns are middlewares on the shared `Pipeline<CommandContext>`. They're not "behaviors" — there's no second mechanism to learn:

```csharp
public sealed class TransactionMiddleware(IDbContext db) : IPipelineMiddleware<CommandContext>
{
    public async Task InvokeAsync(CommandContext ctx, PipelineDelegate<CommandContext> next, CancellationToken ct)
    {
        await using var tx = await db.BeginTransactionAsync(ct);
        await next(ctx, ct);
        if (ctx.Result is { IsSuccess: true })
            await tx.CommitAsync(ct);
        else
            await tx.RollbackAsync(ct);
    }
}

services.AddPipelineMiddleware<CommandContext, TransactionMiddleware>();
```

## 7. Optional batch handler

When you want bulk inserts to actually be one round-trip, opt into batching:

```csharp
public sealed class CreateOrderBatchHandler(IOrderRepository repo) : IBatchCommandHandler<CreateOrder, Guid>
{
    public Task<IReadOnlyList<OperationResult<Guid>>> HandleBatchAsync(
        IReadOnlyList<CreateOrder> commands, CancellationToken ct)
        => repo.CreateManyAsync(commands, ct);
}

services.AddBatchCommandHandler<CreateOrder, Guid, CreateOrderBatchHandler>();
```

`SendBatchAsync` will validate each command (per-handler `ValidateAsync`), forward only the valid ones to the batch handler, and stitch the per-command results back together in input order.

## 8. Observe with OpenTelemetry

```csharp
builder.Services.AddOpenTelemetry().WithTracing(b => b
    .AddSource(CommandActivitySource.Name)
    .AddOtlpExporter());
```

Every dispatch becomes an `Activity` named `Commands.<CommandTypeName>` with a `command.type` tag.

## 9. Validate handlers at startup (optional)

Off by default to keep startup fast. Turn it on when you want missing-handler errors to surface up front:

```csharp
services.AddCommands(o => o.ValidateHandlersAtStartup = true);

// after building the host:
host.Services.ValidateCommandRegistrations();
```

The `ValidateCommandRegistrations()` extension forces the lazy registry to materialize every descriptor.
