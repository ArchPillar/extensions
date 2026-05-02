# ArchPillar.Extensions.Commands

A small, in-process command dispatcher for .NET. Built on
[`ArchPillar.Extensions.Pipelines`](https://www.nuget.org/packages/ArchPillar.Extensions.Pipelines)
and [`ArchPillar.Extensions.Primitives`](https://www.nuget.org/packages/ArchPillar.Extensions.Primitives).
Commands flow through a single shared pipeline; cross-cutting concerns
(validation, transactions, logging) plug in as middlewares. Handlers are
registered with `Microsoft.Extensions.DependencyInjection`. AOT/trim-safe — no
runtime reflection, no `MakeGenericMethod`, no source generators required.

## Why?

The CQRS half nobody loves: dispatching writes. Most projects either reach for
MediatR — which is more than they need and pulls a pipeline-behavior model on
top of an already-existing pipeline — or hand-roll a dispatcher and reinvent
the same plumbing on every project. This package is the in-between: a thin
dispatcher with a clear surface, **no queries** (those go through EF Core
`IQueryable` projection in `ArchPillar.Extensions.Mapper`), no event bus
ambitions, and a hard line about runtime reflection.

## What you get

- **`ICommand`** / **`ICommand<TResult>`** — declare your write operations.
- **`ICommandHandler<TCommand>`** / **`ICommandHandler<TCommand, TResult>`** — implement them. Validation lives on the handler, intentionally.
- **`IBatchCommandHandler<TCommand>`** — opt in to bulk processing for a command type.
- **`CommandHandlerBase<TCommand>`** / **`CommandHandlerBase<TCommand, TResult>`** — optional base classes with status factories (`Ok`, `NotFound`, `Conflict`, …) and assert helpers (`EnsureFound`, `Ensure`, `EnsureAuthorized`).
- **`ICommandDispatcher`** — `SendAsync` and `SendBatchAsync`, always returning an `OperationResult`.
- **Built-in middlewares** — telemetry and exception. Validation is part of the terminal so user middlewares (transactions, locks, retry) wrap both validation and the handler. Wired by `AddCommands()`.

## Quick start

```csharp
public sealed record CreateOrder(string CustomerId, int Quantity) : ICommand<Guid>;

public sealed class CreateOrderHandler(IOrderRepository repository)
    : CommandHandlerBase<CreateOrder, Guid>
{
    public override Task ValidateAsync(CreateOrder cmd, IValidationContext ctx, CancellationToken ct)
    {
        ctx.NotEmpty(cmd.CustomerId, nameof(cmd.CustomerId))
           .Range(cmd.Quantity, 1, 100, nameof(cmd.Quantity));
        return Task.CompletedTask;
    }

    public override async Task<OperationResult<Guid>> HandleAsync(CreateOrder cmd, CancellationToken ct)
    {
        var customer = await repository.FindCustomerAsync(cmd.CustomerId, ct);
        EnsureFound(customer, "Customer not found");

        var orderId = await repository.CreateOrderAsync(customer, cmd.Quantity, ct);
        return Created(orderId);
    }
}
```

```csharp
services.AddCommands();
services.AddCommandHandler<CreateOrder, Guid, CreateOrderHandler>();

// Cross-cutting concerns hook in via the existing Pipelines API:
services.AddPipelineMiddleware<CommandContext, TransactionMiddleware>();

// Dispatch:
OperationResult<Guid> result = await dispatcher.SendAsync(new CreateOrder("cust-1", 3));
if (result.IsSuccess) return Results.Created($"/orders/{result.Value}", null);
return Results.Json(result.Errors, statusCode: (int)result.Status);
```

## Telemetry

`AddCommands()` registers `CommandActivityMiddleware` automatically.
Subscribe via OpenTelemetry to capture every dispatch:

```csharp
builder.Services.AddOpenTelemetry().WithTracing(b => b
    .AddSource(CommandActivitySource.Name)
    .AddOtlpExporter());
```

When no listener is attached the middleware is a zero-allocation pass-through.

## Documentation

Full documentation, design notes, and recipes are in the
[GitHub repository](https://github.com/ArchPillar/extensions/tree/main/docs/commands).

## License

MIT — see the bundled `LICENSE` file.
