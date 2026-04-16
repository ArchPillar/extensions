# ArchPillar.Extensions.Pipelines

A lightweight, **allocation-free** async middleware pipeline for .NET with built-in `Microsoft.Extensions.DependencyInjection` integration. Built on pre-composed nested lambdas: each middleware wraps the rest of the chain, the delegate chain is built once, and the synchronous hot path allocates zero bytes per invocation.

## Why?

Middleware pipelines are everywhere — ASP.NET Core, MediatR behaviours, MassTransit filters — but they are usually tied to a specific framework. `Pipeline<T>` lifts the pattern out into a small, framework-independent class you can use for anything: request handlers, background jobs, domain workflows, data import steps, retry/logging wrappers.

- **Pre-built delegate chain** — composition happens once in the `Pipeline<T>` constructor. Every `ExecuteAsync` is a single delegate invocation with no per-call closure allocation.
- **Allocation-free hot path** — synchronous pipelines (handler returns `Task.CompletedTask`, middlewares tail-call `next`) allocate zero bytes per invocation, verified by unit tests over 1,000 iterations.
- **Async-first** — `Task`-returning throughout, with `CancellationToken` flowing through every step.
- **DI-native** — middlewares and handlers are classes with constructor-injected dependencies. `services.AddPipeline<T, THandler>()` composes `Pipeline<T>` from the `IPipelineMiddleware<T>` and `IPipelineHandler<T>` services registered in the container.
- **No magic** — the core `Pipeline<T>` class is a single constructor and a single `ExecuteAsync` method.

## Quick Start

```csharp
public sealed class LoggingMiddleware(ILogger<LoggingMiddleware> logger) : IPipelineMiddleware<OrderContext>
{
    public async Task InvokeAsync(OrderContext ctx, PipelineDelegate<OrderContext> next, CancellationToken ct)
    {
        logger.LogInformation("Order {Id}: start", ctx.OrderId);
        await next(ctx, ct);
        logger.LogInformation("Order {Id}: done",  ctx.OrderId);
    }
}

public sealed class PlaceOrderHandler : IPipelineHandler<OrderContext>
{
    public Task HandleAsync(OrderContext ctx, CancellationToken ct) => Task.CompletedTask;
}
```

**With DI** — the primary consumption path:

```csharp
using ArchPillar.Extensions.Pipelines;

// A pipeline always has a terminal handler — register it upfront.
services.AddPipeline<OrderContext, PlaceOrderHandler>();

// Middlewares are registered independently. Library modules can contribute
// middlewares to a pipeline owned by the application.
services.AddPipelineMiddleware<OrderContext, LoggingMiddleware>();
services.AddPipelineMiddleware<OrderContext, ValidationMiddleware>();
services.AddPipelineMiddleware<OrderContext, TransactionMiddleware>();

// Resolve Pipeline<OrderContext> anywhere
public sealed class OrderEndpoint(Pipeline<OrderContext> pipeline)
{
    public Task HandleAsync(OrderContext ctx, CancellationToken ct)
        => pipeline.ExecuteAsync(ctx, ct);
}
```

**Direct (no DI)** — useful in tests and small programs:

```csharp
var pipeline = Pipeline
    .For<OrderContext>()
    .Use(new LoggingMiddleware(logger))
    .Use(async (ctx, next, ct) =>
    {
        if (ctx.ShouldStop) return;    // short-circuit
        await next(ctx, ct);
    })
    .Handle(new PlaceOrderHandler())
    .Build();

await pipeline.ExecuteAsync(context);
```

## DI extension surface

Five extension methods on `IServiceCollection`:

- **`AddPipeline<T, THandler>(pipelineLifetime?, handlerLifetime?)`** — registers `Pipeline<T>` and a DI-resolved handler, each with its own lifetime.
- **`AddPipeline<T>(IPipelineHandler<T> instance, pipelineLifetime?)`** — registers `Pipeline<T>` with a pre-built handler instance (use `PipelineHandler.FromDelegate(...)` to wrap a delegate).
- **`AddPipelineMiddleware<T, TMiddleware>(lifetime?)`** — registers a middleware without touching the pipeline registration. Library modules use this to contribute middlewares to pipelines owned by the application.
- **`ReplacePipelineHandler<T, THandler>(lifetime?)`** — swaps the handler registration. Useful in integration tests.
- **`AddPipelineTelemetry<T>(lifetime?)`** — shortcut for `AddPipelineMiddleware<T, ActivityMiddleware<T>>`. Requires `T : IPipelineContext`.

Cross-registration lifetime validation is left to the container — enable `ValidateScopes` and `ValidateOnBuild` on `ServiceProviderOptions` to catch captive dependencies.

## Built-in distributed tracing

Opt into telemetry by implementing `IPipelineContext` on your context type (only `OperationName` is required; `ActivityKind`, `ParentContext`, and `EnrichActivity` have default implementations) and register `ActivityMiddleware<T>` in the pipeline:

```csharp
public sealed class OrderContext : IPipelineContext
{
    public int OrderId { get; set; }

    public string OperationName => "Orders.Place";

    public void EnrichActivity(Activity activity)
        => activity.SetTag("order.id", OrderId);
}

services.AddPipelineTelemetry<OrderContext>();
```

Activities flow through `PipelineActivitySource.Name` (`"ArchPillar.Extensions.Pipelines"`) — subscribe with OpenTelemetry or a raw `ActivityListener`. When no subscriber is attached, the middleware is a zero-allocation pass-through.

## Documentation

Full documentation and examples are available at the [GitHub repository](https://github.com/ArchPillar/extensions).
