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

## Isolation

`AddPipeline<T>()` registers middlewares by their **concrete type**, not as `IPipelineMiddleware<T>`. That means:

- Two different pipelines for the same `T` don't accidentally share middlewares.
- Registering `IPipelineMiddleware<T>` elsewhere in the container does not attach it to the pipeline built here.
- `sp.GetServices<IPipelineMiddleware<T>>()` is not polluted by the pipeline registration.

## Documentation

Full documentation and examples are available at the [GitHub repository](https://github.com/ArchPillar/extensions).
