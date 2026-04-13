# ArchPillar.Extensions.Pipelines

A lightweight, DI-friendly, **allocation-free** async middleware pipeline for .NET. Built on pre-composed nested lambdas: each middleware wraps the rest of the chain, the delegate chain is built once, and the synchronous hot path allocates zero bytes per invocation.

## Why?

Middleware pipelines are everywhere — ASP.NET Core, MediatR behaviours, MassTransit filters — but they are usually tied to a specific framework. `Pipeline<T>` lifts the pattern out into a small, framework-independent class you can use for anything: request handlers, background jobs, domain workflows, data import steps, retry/logging wrappers.

- **DI-native** — middlewares and handlers are classes with constructor-injected dependencies. Register them with `services.AddPipeline<T>()` (see `ArchPillar.Extensions.Pipelines.DependencyInjection`) and the pipeline is ready to inject anywhere.
- **Pre-built delegate chain** — composition happens once in the `Pipeline<T>` constructor. Every `ExecuteAsync` is a single delegate invocation with no per-call closure allocation.
- **Allocation-free hot path** — synchronous pipelines (handler returns `Task.CompletedTask`, middlewares tail-call `next`) allocate zero bytes per invocation, verified by unit tests over 1,000 iterations.
- **Async-first** — `Task`-returning throughout, with `CancellationToken` flowing through every step.
- **No magic** — the core `Pipeline<T>` class is a single constructor and a single `ExecuteAsync` method.

## Quick Start

```csharp
public interface IOrderContext
{
    int OrderId { get; }
    bool ShouldStop { get; set; }
}

public sealed class LoggingMiddleware(ILogger<LoggingMiddleware> logger) : IPipelineMiddleware<IOrderContext>
{
    public async Task InvokeAsync(IOrderContext ctx, PipelineDelegate<IOrderContext> next, CancellationToken ct)
    {
        logger.LogInformation("Order {Id}: start", ctx.OrderId);
        await next(ctx, ct);
        logger.LogInformation("Order {Id}: done",  ctx.OrderId);
    }
}

public sealed class PlaceOrderHandler : IPipelineHandler<IOrderContext>
{
    public Task HandleAsync(IOrderContext ctx, CancellationToken ct)
    {
        // ... do the actual work
        return Task.CompletedTask;
    }
}
```

**Direct (no DI)** — useful in tests and small programs:

```csharp
var pipeline = Pipeline
    .For<IOrderContext>()
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

**With DI** — install `ArchPillar.Extensions.Pipelines.DependencyInjection` and use `services.AddPipeline<T>()`:

```csharp
services.AddPipeline<IOrderContext>()
    .Use<LoggingMiddleware>()
    .Use<ValidationMiddleware>()
    .Use<TransactionMiddleware>()
    .Handle<PlaceOrderHandler>();

// Inject where needed
public sealed class OrderEndpoint(Pipeline<IOrderContext> pipeline)
{
    public Task Handle(IOrderContext ctx) => pipeline.ExecuteAsync(ctx);
}
```

## Documentation

Full documentation and examples are available at the [GitHub repository](https://github.com/ArchPillar/extensions).
