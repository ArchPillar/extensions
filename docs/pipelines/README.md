# ArchPillar.Extensions.Pipelines

A lightweight, DI-friendly, **allocation-free** async middleware pipeline for .NET. Built on pre-composed nested lambdas: each middleware wraps the rest of the chain, the delegate chain is built once, and the synchronous hot path allocates zero bytes per invocation.

## Packages

- **`ArchPillar.Extensions.Pipelines`** — BCL-only core. Contains the contracts (`IPipelineHandler<T>`, `IPipelineMiddleware<T>`, `PipelineDelegate<T>`), the `Pipeline<T>` class, the static `Pipeline.For<T>()` entry point, the `PipelineBuilder<T>`, and delegate-based helpers.
- **`ArchPillar.Extensions.Pipelines.DependencyInjection`** — `Microsoft.Extensions.DependencyInjection` integration. Adds `AddPipeline<T, THandler>()`, `AddPipelineMiddleware<T, TMiddleware>()`, and `ReplacePipelineHandler<T, THandler>()` extensions with constructor injection into middlewares and handler.

## Why?

Middleware pipelines are everywhere — ASP.NET Core, MediatR behaviours, MassTransit filters — but they are usually tied to a specific framework. `Pipeline<T>` lifts the pattern out into a small, framework-independent class you can use for anything: request handlers, background jobs, domain workflows, data import steps, retry/logging wrappers.

## Design highlights

- **Nested-lambda model.** Each middleware wraps the rest of the pipeline. Short-circuit by not calling `next(...)`. Add error boundaries with `try/catch await next(...)`.
- **Pre-built delegate chain.** The middleware → handler composition is built **once** in the `Pipeline<T>` constructor. Every subsequent `ExecuteAsync` is a single delegate invocation.
- **Allocation-free hot path.** Synchronous pipelines (handler returns a completed task, middlewares tail-call `next`) allocate **zero bytes per invocation**. Locked in by unit tests using `GC.GetAllocatedBytesForCurrentThread()`.
- **DI-native.** Middlewares and handlers are classes with constructor-injected dependencies. `services.AddPipeline<T>()` composes a `Pipeline<T>` from the `IPipelineMiddleware<T>` and `IPipelineHandler<T>` services registered in the container, so multiple modules can contribute to the same pipeline without coordination.
- **Async-first.** `Task`-returning throughout, with `CancellationToken` flowing through every step.
- **Reusable.** A single `Pipeline<T>` instance is safe to invoke many times, concurrently, as long as the underlying handler and middlewares themselves are thread-safe.

## Quick start (direct, no DI)

```csharp
using ArchPillar.Extensions.Pipelines;

var pipeline = Pipeline
    .For<OrderContext>()
    .Use(async (ctx, next, ct) =>
    {
        Console.WriteLine($"start {ctx.OrderId}");
        await next(ctx, ct);
        Console.WriteLine($"done  {ctx.OrderId}");
    })
    .Use(async (ctx, next, ct) =>
    {
        if (ctx.OrderId <= 0) return;   // short-circuit
        await next(ctx, ct);
    })
    .Handle(ctx => Console.WriteLine($"handler: {ctx.OrderId}"))
    .Build();

await pipeline.ExecuteAsync(new OrderContext { OrderId = 42 });
```

## Quick start (with DI)

```csharp
using ArchPillar.Extensions.Pipelines;
using Microsoft.Extensions.DependencyInjection;

services.AddPipeline<OrderContext, PlaceOrderHandler>();
services.AddPipelineMiddleware<OrderContext, LoggingMiddleware>();
services.AddPipelineMiddleware<OrderContext, ValidationMiddleware>();
services.AddPipelineMiddleware<OrderContext, TransactionMiddleware>();

// Inject Pipeline<OrderContext> anywhere:
public sealed class OrderEndpoint(Pipeline<OrderContext> pipeline)
{
    public Task Handle(OrderContext ctx, CancellationToken ct)
        => pipeline.ExecuteAsync(ctx, ct);
}
```

A class-based middleware looks exactly like the ASP.NET Core middleware you are used to, with one difference: `next` takes the context and cancellation token explicitly (so the chain is pre-built and zero-allocation at runtime):

```csharp
public sealed class LoggingMiddleware(ILogger<LoggingMiddleware> logger) : IPipelineMiddleware<OrderContext>
{
    public async Task InvokeAsync(OrderContext ctx, PipelineDelegate<OrderContext> next, CancellationToken ct)
    {
        logger.LogInformation("start {OrderId}", ctx.OrderId);
        await next(ctx, ct);
        logger.LogInformation("done  {OrderId}", ctx.OrderId);
    }
}
```

## Documentation

- **[Getting Started](getting-started.md)** — installation, first pipeline (direct and DI), writing class-based and lambda-based middleware.
- **[Specification](SPEC.md)** — design philosophy, contracts, behaviour guarantees, allocation model.

## Samples

- **[samples/Pipelines/Pipeline.BuilderSample](../../samples/Pipelines/Pipeline.BuilderSample/)** — a standalone console program that builds a pipeline directly with `Pipeline.For<T>()...Build()`. No DI container.
- **[samples/Pipelines/Pipeline.HostSample](../../samples/Pipelines/Pipeline.HostSample/)** — a `Microsoft.Extensions.Hosting` console app that wires the same pipeline through `services.AddPipeline<T>()` with `ILogger<T>` constructor injection into middlewares and handler.
