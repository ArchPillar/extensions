# ArchPillar.Extensions.Pipelines.DependencyInjection

`Microsoft.Extensions.DependencyInjection` integration for `ArchPillar.Extensions.Pipelines`. Adds a fluent `services.AddPipeline<T>()` registration for `Pipeline<T>` middlewares and handlers.

## Quick Start

```csharp
using ArchPillar.Extensions.Pipelines;

services.AddPipeline<OrderContext>()
    .Use<LoggingMiddleware>()
    .Use<ValidationMiddleware>()
    .Use<TransactionMiddleware>()
    .Handle<PlaceOrderHandler>();

// Resolve Pipeline<OrderContext> anywhere
public sealed class OrderEndpoint(Pipeline<OrderContext> pipeline)
{
    public Task HandleAsync(OrderContext ctx, CancellationToken ct)
        => pipeline.ExecuteAsync(ctx, ct);
}
```

Middlewares execute in the order they were registered. Each middleware and the handler resolve their own constructor dependencies from DI just like any other registered class.

Pipelines are scoped by default. Override via `services.AddPipeline<T>(ServiceLifetime.Singleton)` if you need long-lived pipelines (ensure your middlewares and handler are singleton-safe).

## Composition via DI

`Pipeline<T>` is resolved by the container using its public constructor:

```csharp
public Pipeline(IPipelineHandler<T> handler, IEnumerable<IPipelineMiddleware<T>> middlewares)
```

Both collaborators come from the container. `.Use<TMiddleware>()` writes an `IPipelineMiddleware<T>` → `TMiddleware` descriptor via `TryAddEnumerable`; `.Handle<THandler>()` writes `IPipelineHandler<T>` → `THandler` via `TryAdd`. That means:

- **Multiple modules can contribute middlewares to the same pipeline.** Two unrelated calls to `services.AddPipeline<T>().Use<X>()` compose cleanly — the resulting pipeline contains the union of all registered middlewares.
- **External `services.AddScoped<IPipelineMiddleware<T>, X>()` registrations are respected** and show up in the pipeline in registration order.
- **The builder holds no captured state.** Once `AddPipeline<T>()` returns, the builder can be discarded; everything that drives the pipeline's behaviour lives as plain service descriptors in the container.

## Forgiving registration

Every registration is idempotent:

- `AddPipeline<T>()` called twice → one `Pipeline<T>` descriptor (second call is a no-op).
- `.Use<M>()` called twice for the same `M` → one `IPipelineMiddleware<T>` descriptor (deduplicated by `TryAddEnumerable`).
- `.Handle<H1>()` then `.Handle<H2>()` → `H1` wins (first call registers, subsequent `TryAdd`s are no-ops).

This makes it safe for library code to contribute middlewares to a pipeline owned by application code without having to detect or care about existing registrations.

## Documentation

Full documentation and examples are available at the [GitHub repository](https://github.com/ArchPillar/extensions).
