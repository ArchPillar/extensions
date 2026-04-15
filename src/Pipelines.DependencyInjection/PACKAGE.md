# ArchPillar.Extensions.Pipelines.DependencyInjection

`Microsoft.Extensions.DependencyInjection` integration for `ArchPillar.Extensions.Pipelines`. Provides `AddPipeline<T, THandler>()`, `AddPipelineMiddleware<T, TMiddleware>()`, and `ReplacePipelineHandler<T, THandler>()` extension methods.

## Quick Start

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

Middlewares execute in registration order. Each middleware and the handler resolve their own constructor dependencies from DI like any other registered class.

## Delegate handlers

Use an inline delegate when a class handler is overkill — for tests, adapters, or trivial sinks:

```csharp
services.AddPipeline<OrderContext>((ctx, ct) => Task.CompletedTask);           // async + token
services.AddPipeline<OrderContext>(ctx => SomeTask(ctx));                      // async
services.AddPipeline<OrderContext>(ctx => ctx.Handled = true);                 // sync
```

Delegate handlers are registered as singleton instances (the delegate itself is stateless).

## Replacing the handler

`ReplacePipelineHandler<T, THandler>()` removes any existing handler registration and installs a new one. This is useful in tests that need to swap out the production handler:

```csharp
services.ReplacePipelineHandler<OrderContext, FakeOrderHandler>();
// or with a delegate:
services.ReplacePipelineHandler<OrderContext>((ctx, ct) => Task.CompletedTask);
```

## Lifetimes

Every registration accepts its own `ServiceLifetime`. The pipeline, each middleware, and the handler are independent:

```csharp
services.AddPipeline<OrderContext, PlaceOrderHandler>(
    pipelineLifetime: ServiceLifetime.Scoped,
    handlerLifetime: ServiceLifetime.Scoped);

services.AddPipelineMiddleware<OrderContext, LoggingMiddleware>(ServiceLifetime.Singleton);
services.AddPipelineMiddleware<OrderContext, AuditMiddleware>(ServiceLifetime.Scoped);
```

The only combination that is rejected at registration time is a **singleton `Pipeline<T>` with a scoped middleware or handler** — the classic captive-dependency bug. The validation fires whichever order you register in: if the pipeline is already singleton, adding a scoped step throws; if a scoped step is already registered, promoting the pipeline to singleton throws.

## Composition via DI

`Pipeline<T>` is resolved by the container using its public constructor:

```csharp
public Pipeline(IPipelineHandler<T> handler, IEnumerable<IPipelineMiddleware<T>> middlewares)
```

Both collaborators come from the container:

- **Multiple modules can contribute middlewares to the same pipeline.** `AddPipelineMiddleware<T, X>()` writes an `IPipelineMiddleware<T>` → `X` descriptor via `TryAddEnumerable`.
- **External `services.AddScoped<IPipelineMiddleware<T>, X>()` registrations are respected** and show up in the pipeline in registration order.
- **Registration calls are idempotent by type.** `AddPipelineMiddleware<T, X>()` called twice for the same `X` dedupes to a single descriptor. `AddPipeline<T, H>()` called twice is a no-op (first handler wins). To change a handler, use `ReplacePipelineHandler<T, …>()`.

## Documentation

Full documentation and examples are available at the [GitHub repository](https://github.com/ArchPillar/extensions).
