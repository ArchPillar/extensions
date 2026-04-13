# ArchPillar.Extensions.Primitives

A collection of small, dependency-free .NET primitives. Each primitive is a single self-contained unit — zero shared machinery, no hidden coupling, no runtime reflection. Start using one, ignore the rest.

## Current primitives

| Primitive     | Description                                                                                                                       |
| ------------- | --------------------------------------------------------------------------------------------------------------------------------- |
| `Pipeline<T>` | A lightweight, DI-friendly, **allocation-free** async middleware pipeline built on pre-composed nested lambdas. |

More primitives will be added as concrete needs appear. Each lives in its own feature folder under `src/Primitives/` and shares the flat `ArchPillar.Extensions.Primitives` namespace.

## Packages

- **`ArchPillar.Extensions.Primitives`** — BCL-only core. Contains every primitive's contracts, types, and direct-use builders.
- **`ArchPillar.Extensions.Primitives.DependencyInjection`** — `Microsoft.Extensions.DependencyInjection` integration. Adds `services.AddPipeline<T>()` (and future equivalents) for type-based registration.

## `Pipeline<T>`

Middleware pipelines are everywhere — ASP.NET Core, MediatR behaviours, MassTransit filters — but they are usually tied to a specific framework. `Pipeline<T>` lifts the pattern out into a ~100-line class you can use for anything: request handlers, background jobs, domain workflows, data import steps, retry/logging wrappers.

### Design highlights

- **Nested-lambda model.** Each middleware wraps the rest of the pipeline. Short-circuit by not calling `next(...)`. Add error boundaries with `try/catch await next(...)`.
- **Pre-built delegate chain.** The middleware → handler composition is built **once** in the `Pipeline<T>` constructor. Every subsequent `ExecuteAsync` is a single delegate invocation.
- **Allocation-free hot path.** Synchronous pipelines (handler returns a completed task, middlewares tail-call `next`) allocate **zero bytes per invocation**. Locked in by unit tests using `GC.GetAllocatedBytesForCurrentThread()`.
- **DI-native.** Middlewares and handlers are classes with constructor-injected dependencies. `services.AddPipeline<T>()` registers them by their concrete types so pipelines stay isolated from the global `IPipelineMiddleware<T>` service namespace.
- **Async-first.** `Task`-returning throughout, with `CancellationToken` flowing through every step.
- **Reusable.** A single `Pipeline<T>` instance is safe to invoke many times, concurrently, as long as the underlying handler and middlewares themselves are thread-safe.

### Quick start (direct, no DI)

```csharp
using ArchPillar.Extensions.Primitives;

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

### Quick start (with DI)

```csharp
using ArchPillar.Extensions.Primitives;
using Microsoft.Extensions.DependencyInjection;

services.AddPipeline<OrderContext>()
    .Use<LoggingMiddleware>()
    .Use<ValidationMiddleware>()
    .Use<TransactionMiddleware>()
    .Handle<PlaceOrderHandler>();

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

- **[samples/Primitives/Pipeline.BuilderSample](../../samples/Primitives/Pipeline.BuilderSample/)** — a standalone console program that builds a pipeline directly with `Pipeline.For<T>()...Build()`. No DI container.
- **[samples/Primitives/Pipeline.HostSample](../../samples/Primitives/Pipeline.HostSample/)** — a `Microsoft.Extensions.Hosting` console app that wires the same pipeline through `services.AddPipeline<T>()` with `ILogger<T>` constructor injection into middlewares and handler.
