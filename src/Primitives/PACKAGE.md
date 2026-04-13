# ArchPillar.Extensions.Primitives

A collection of small, dependency-free .NET primitives. The first one is `Pipeline<T>` — a lightweight, DI-friendly, async middleware pipeline built on nested lambdas.

## Why?

Middleware pipelines are everywhere — ASP.NET Core, MediatR behaviours, MassTransit filters — but they are usually tied to a specific framework. `Pipeline<T>` lifts the pattern out into a single 100-line class you can use for anything: request handlers, background jobs, domain workflows, data import steps, retry/logging wrappers.

- **DI-native** — middlewares and handlers are classes with constructor-injected dependencies. Register them with `services.AddPipeline<T>()` (see `ArchPillar.Extensions.Primitives.DependencyInjection`) and the pipeline is ready to inject anywhere.
- **Async-first** — `Task`-returning throughout, with `CancellationToken` flowing through every step.
- **Nested-lambda model** — each middleware wraps the rest of the pipeline. Short-circuit by not calling `next()`; add error boundaries with `try/catch await next()`.
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

**With DI** — install `ArchPillar.Extensions.Primitives.DependencyInjection` and use `services.AddPipeline<T>()`:

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
