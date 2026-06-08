# ArchPillar.Extensions.Commands

A small, in-process command dispatcher for .NET. Commands flow through a single
shared pipeline; cross-cutting concerns (validation, transactions, logging) plug
in as middlewares. Every dispatch returns an `OperationResult` /
`OperationResult<TResult>` — writes are always awaited and observed, never queued
or discarded. AOT/trim-safe: no runtime reflection, no `MakeGenericMethod`, no
source generators required.

## Why?

The CQRS half nobody loves: dispatching writes. Most projects either reach for
MediatR — which is more than they need and pulls a pipeline-behaviour model on top
of an already-existing pipeline — or hand-roll a dispatcher and reinvent the same
plumbing on every project. This library is the in-between: a thin dispatcher with
a clear surface, **no queries**, **no event bus** ambitions, and a hard line about
runtime reflection.

It does not invent its own machinery. The dispatch path is a
[`ArchPillar.Extensions.Pipelines`](../pipelines/) `Pipeline<CommandContext>`, so
cross-cutting concerns are ordinary `IPipelineMiddleware<CommandContext>`
classes — the same contract you would use for any other pipeline, written once and
applied to every command. Results, validation errors, and exceptions are carried
by the `OperationResult` / `OperationProblem` types from
[`ArchPillar.Extensions.Primitives`](../primitives/), so a command outcome already
has the RFC 7807 `application/problem+json` shape an API boundary wants. Both are
pulled in as dependencies.

## Quick Start

```csharp
using ArchPillar.Extensions.Commands;
using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Operations;

public sealed record CreateOrder(string Customer, int Quantity) : ICommand<Guid>;

public sealed class CreateOrderHandler(OrderContext context)
    : CommandHandlerBase<CreateOrder, Guid>
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
        EnsureFound(customer, "Customer not found.");        // throws → caught by the exception middleware

        var order = new Order(customer, command.Quantity);
        context.Orders.Add(order);
        await context.SaveChangesAsync(cancellationToken);
        return Created(order.Id);                            // factory returns OperationResult<Guid>
    }
}
```

```csharp
using ArchPillar.Extensions.Commands;
using ArchPillar.Extensions.Pipelines;
using Microsoft.Extensions.DependencyInjection;

services.AddCommands();
services.AddCommandHandler<CreateOrder, Guid, CreateOrderHandler>();

// Cross-cutting concerns hook in via the existing Pipelines API:
services.AddPipelineMiddleware<CommandContext, TransactionMiddleware>();

// Dispatch:
OperationResult<Guid> result = await dispatcher.SendAsync(new CreateOrder("alice", 3));
if (result.IsSuccess)
{
    return Results.Created($"/orders/{result.Value}", null);
}

return Results.Json(result.Problem, statusCode: (int)result.Status);
```

## Features

| Feature | What it gives you |
| --- | --- |
| [Command dispatch](features.md#command-dispatch) | `ICommand` / `ICommand<TResult>` through `ICommandDispatcher.SendAsync`, always returning an `OperationResult`. |
| [Handlers](features.md#handlers) | One handler per command via `ICommandHandler<TCommand>[, TResult]`, with optional `CommandHandlerBase` status factories and assert helpers. |
| [Validation](features.md#validation) | Handler-side `ValidateAsync` with composable helpers, `[CallerArgumentExpression]` field capture, and automatic status precedence. |
| [Cross-cutting middleware](features.md#cross-cutting-middleware) | Transactions, logging, authorization, retry as `IPipelineMiddleware<CommandContext>` on the shared pipeline. |
| [Result transport and exceptions](features.md#result-transport-and-exceptions) | Return or throw failures; the exception middleware unwraps `OperationException` and synthesizes 500s. |
| [Batch handling](features.md#batch-handling) | `SendBatchAsync` with an optional `IBatchCommandHandler` for atomic bulk writes. |
| [Telemetry](features.md#telemetry) | An `Activity` per dispatch on the Commands-owned `ActivitySource`; zero-allocation when unsubscribed. |
| [Cancellation](features.md#cancellation) | `CancellationToken` flows through every middleware and handler; cancellation propagates unchanged. |
| [DI registration](features.md#di-registration) | `AddCommands()` + explicit `AddCommandHandler<...>()`; lazy routing and optional startup validation. |

## Documentation

- [Getting started](getting-started.md) — install-to-first-dispatch walkthrough: declare commands, write handlers, wire DI, observe the result.
- [Features](features.md) — every feature with a runnable example.
- [Recommendations](recommendations.md) — production patterns: Minimal-API wiring, transaction middleware, error projection, batching.
- [Specification](./internals/SPEC.md) — the design contract: goals, non-goals, conceptual model, error philosophy.

## Samples

- [samples/Commands/Commands.HostSample](../../samples/Commands/Commands.HostSample/) — a `Microsoft.Extensions.Hosting` console app dispatching a result-bearing and a no-result command, with validation, the `throw OperationResult` path, telemetry, and an optional batch handler.
- [samples/Commands/Commands.WebApiSample](../../samples/Commands/Commands.WebApiSample/) — an ASP.NET Core Minimal-API Notes service with EF Core (SQLite in-memory), a `TransactionMiddleware`, batching, and telemetry wired up.
