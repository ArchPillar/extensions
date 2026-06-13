# ArchPillar.Extensions.Pipelines

A lightweight, DI-friendly, **allocation-free** async middleware pipeline for .NET. Built on pre-composed nested lambdas: each middleware wraps the rest of the chain, the delegate chain is built once, and the synchronous hot path allocates zero bytes per invocation.

## Why?

Middleware pipelines are everywhere — ASP.NET Core middleware, MediatR behaviours, MassTransit filters — but they are usually tied to a specific framework. `Pipeline<T>` lifts the pattern out into a small, framework-independent class you can use for anything: request handlers, background jobs, domain workflows, data import steps, retry/logging wrappers.

The design choice that distinguishes it is composition happening **once**, in the `Pipeline<T>` constructor: the middleware-to-handler chain is pre-built into a single delegate, so every `ExecuteAsync` is one delegate invocation with no per-call closure allocation. The opposing approach — rebuilding the chain per call, or discovering middlewares by reflection — trades that predictability away. Here nothing scans assemblies, nothing inspects attributes, and the set of middlewares in a pipeline is exactly the set you registered. The only runtime dependency is `Microsoft.Extensions.DependencyInjection.Abstractions`, referenced privately, so consumers who use `Pipeline<T>` directly never see it.

## Quick Start

**With DI** — the primary consumption path:

```csharp
using ArchPillar.Extensions.Pipelines;
using Microsoft.Extensions.DependencyInjection;

// A pipeline always has a terminal handler — register it upfront.
services.AddPipeline<OrderContext, PlaceOrderHandler>();

// Middlewares are registered independently. Library modules can contribute
// middlewares to a pipeline owned by the application.
services.AddPipelineMiddleware<OrderContext, LoggingMiddleware>();
services.AddPipelineMiddleware<OrderContext, ValidationMiddleware>();

// Resolve Pipeline<OrderContext> anywhere via constructor injection.
public sealed class OrderEndpoint(Pipeline<OrderContext> pipeline)
{
    public Task HandleAsync(OrderContext context, CancellationToken cancellationToken)
        => pipeline.ExecuteAsync(context, cancellationToken);
}
```

A class-based middleware looks like ASP.NET Core middleware, with one difference: `next` takes the context and cancellation token explicitly, so the chain is pre-built and zero-allocation at runtime:

```csharp
public sealed class LoggingMiddleware(ILogger<LoggingMiddleware> logger) : IPipelineMiddleware<OrderContext>
{
    public async Task InvokeAsync(OrderContext context, PipelineDelegate<OrderContext> next, CancellationToken cancellationToken)
    {
        logger.LogInformation("start {OrderId}", context.OrderId);
        await next(context, cancellationToken);
        logger.LogInformation("done  {OrderId}", context.OrderId);
    }
}
```

**Direct (no DI)** — useful in tests and small programs:

```csharp
var pipeline = Pipeline
    .For<OrderContext>()
    .Use(new LoggingMiddleware(logger))
    .Use(async (context, next, cancellationToken) =>
    {
        if (context.OrderId <= 0)
        {
            return; // short-circuit
        }

        await next(context, cancellationToken);
    })
    .Handle(new PlaceOrderHandler())
    .Build();

await pipeline.ExecuteAsync(new OrderContext { OrderId = 42 });
```

## Features

| Feature | Description |
| --- | --- |
| [Pipeline composition](features.md#pipeline-composition-middleware-and-a-terminal-handler) | Middlewares wrap a single terminal handler over a shared context `T`. |
| [Short-circuiting](features.md#short-circuiting) | A middleware that does not call `next(...)` skips every downstream step. |
| [Error boundaries and cancellation](features.md#error-boundaries-and-cancellation) | Exceptions propagate outward; `CancellationToken` flows through every step. |
| [`PipelineBuilder<T>`](features.md#the-pipelinebuildert-and-the-pipelinefort-entry-point) | Fluent `Pipeline.For<T>()...Build()` for wiring pipelines without DI. |
| [Delegate middleware and handlers](features.md#delegate-based-middleware-and-handlers) | Use lambdas via `Use(...)` / `Handle(...)` and the `FromDelegate` factories. |
| [DI integration](features.md#dependency-injection-integration) | Five `IServiceCollection` extensions register and compose the pipeline. |
| [Middleware ordering](features.md#middleware-ordering) | Middlewares run in registration order, outermost-first. |
| [Distributed tracing](features.md#distributed-tracing) | `IPipelineContext` + `ActivityMiddleware<T>` emit `System.Diagnostics.Activity` traces. |
| [Zero-allocation hot path](features.md#the-zero-allocation-synchronous-hot-path) | Synchronous pipelines allocate zero bytes per invocation. |
| [Snapshot semantics](features.md#snapshot-semantics-and-reuse) | The middleware list is frozen at construction; instances are reusable. |
| [Fail-fast construction](features.md#fail-fast-construction) | Null steps and missing handlers throw at build time, not query time. |

## Performance

`Pipeline<T>` is benchmarked with BenchmarkDotNet under
[`benchmarks/Pipelines.Benchmarks`](../../benchmarks/Pipelines.Benchmarks/). The
benchmark measures the engine's own overhead on the synchronous hot path — trivial
pass-through middlewares that tail-call `next`, and a handler that returns a cached
`Task.CompletedTask` — across 0, 1, 3, and 10 middleware layers, against a direct
handler call as the baseline.

The two properties the benchmark exists to lock in:

- **Every scenario allocates zero bytes** (the `Allocated` column is `0 B`), the same
  guarantee `PipelineAllocationTests` enforces in the test suite.
- **Per-layer overhead is single-digit nanoseconds** — a pre-built delegate chain
  invoked once per execution.

Run it locally:

```bash
dotnet run -c Release --project benchmarks/Pipelines.Benchmarks
```

## Documentation

- **[Getting Started](getting-started.md)** — installation, your first pipeline (direct and DI), writing class-based and lambda-based middleware, telemetry.
- **[Features](features.md)** — every feature, with a compilable example each.
- **[Recommendations](recommendations.md)** — production patterns: middleware ordering, lifetimes, the allocation model, error boundaries.
- **[Specification](internals/SPEC.md)** — the design contract: goals, contracts, behaviour guarantees, allocation model.

## Samples

- **[Pipelines.BuilderSample](../../samples/Pipelines/Pipelines.BuilderSample/)** — a standalone console program that builds a pipeline directly with `Pipeline.For<T>()...Build()`. No DI container; combines lambda and class-based middleware with a class-based handler, and shows the short-circuit path.
- **[Pipelines.HostSample](../../samples/Pipelines/Pipelines.HostSample/)** — a `Microsoft.Extensions.Hosting` console app that wires the same pipeline through `services.AddPipeline<T>()` with `ILogger<T>` injected into every middleware and the handler, plus `AddPipelineTelemetry<T>()` and an `ActivityListener` printing each activity.
