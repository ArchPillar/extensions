# Getting Started — `Pipeline<T>`

This walks you through installing `ArchPillar.Extensions.Primitives`, writing your first pipeline, and wiring it up both manually and through `Microsoft.Extensions.DependencyInjection`.

## 1. Install

The library ships as two NuGet packages:

```bash
# Core — BCL only
dotnet add package ArchPillar.Extensions.Primitives

# Optional — Microsoft.Extensions.DependencyInjection integration
dotnet add package ArchPillar.Extensions.Primitives.DependencyInjection
```

You can use the core package on its own — the DI package is needed only if you want the fluent `services.AddPipeline<T>()` registration helper.

## 2. Define your context type

The context is the thing that flows through the pipeline. It can be any type — a plain POCO, a record, an interface, whatever fits your workflow.

```csharp
public sealed class OrderContext
{
    public int OrderId { get; set; }
    public int UserId { get; set; }
    public bool Authorized { get; set; }
}
```

## 3. Write a handler

The handler is the terminal step — every pipeline has exactly one, and it runs last.

```csharp
using ArchPillar.Extensions.Primitives;

public sealed class PlaceOrderHandler : IPipelineHandler<OrderContext>
{
    public Task HandleAsync(OrderContext context, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"placed order {context.OrderId}");
        return Task.CompletedTask;
    }
}
```

## 4. Write a middleware (class-based)

A middleware wraps the rest of the pipeline. Call `next(context, cancellationToken)` to continue; skip it to short-circuit.

```csharp
public sealed class ValidationMiddleware : IPipelineMiddleware<OrderContext>
{
    public async Task InvokeAsync(OrderContext ctx, PipelineDelegate<OrderContext> next, CancellationToken ct)
    {
        if (ctx.OrderId <= 0)
        {
            Console.WriteLine("invalid order");
            return; // short-circuit
        }

        await next(ctx, ct);
    }
}
```

### Why does `next` take `(ctx, ct)` explicitly?

Unlike ASP.NET Core's `Func<Task> next`, `Pipeline<T>`'s `PipelineDelegate<T> next` is a pre-built delegate that takes the context and cancellation token as arguments. The chain is composed **once** in the `Pipeline<T>` constructor; invoking `next(ctx, ct)` is a single delegate call, not a per-invocation allocation. This keeps the synchronous hot path allocation-free.

## 5a. Build a pipeline without DI

For tests, tools, or apps that manage their own dependencies, use the fluent `Pipeline.For<T>()` builder:

```csharp
var pipeline = Pipeline
    .For<OrderContext>()
    .Use(new ValidationMiddleware())
    .Use(async (ctx, next, ct) =>                 // lambda middleware
    {
        Console.WriteLine($"start {ctx.OrderId}");
        await next(ctx, ct);
        Console.WriteLine($"done  {ctx.OrderId}");
    })
    .Handle(new PlaceOrderHandler())
    .Build();

await pipeline.ExecuteAsync(new OrderContext { OrderId = 42 });
```

Middlewares execute in the order you added them, wrapping outward → inward. `Handle(...)` sets the terminal step and can also take a plain delegate (`Action<T>`, `Func<T, Task>`, or `Func<T, CancellationToken, Task>`).

## 5b. Build a pipeline with DI

For host-based applications (`Microsoft.Extensions.Hosting`, ASP.NET Core, etc.), register the pipeline in your `IServiceCollection` so every middleware and the handler get full constructor injection:

```csharp
using ArchPillar.Extensions.Primitives;
using Microsoft.Extensions.DependencyInjection;

services.AddPipeline<OrderContext>()
    .Use<LoggingMiddleware>()
    .Use<ValidationMiddleware>()
    .Use<AuthorizationMiddleware>()
    .Handle<PlaceOrderHandler>();
```

Now any component can take `Pipeline<OrderContext>` as a constructor dependency:

```csharp
public sealed class OrderEndpoint(Pipeline<OrderContext> pipeline)
{
    public Task HandleAsync(OrderContext ctx, CancellationToken ct)
        => pipeline.ExecuteAsync(ctx, ct);
}
```

The middlewares and the handler are registered in the DI container by their **concrete type**, not as `IPipelineMiddleware<OrderContext>`. This keeps your pipeline isolated from the rest of the container — see the [SPEC](SPEC.md#isolation) for details.

### Lifetime knob

`AddPipeline<T>()` defaults to `ServiceLifetime.Scoped`. Pass `ServiceLifetime.Singleton` or `ServiceLifetime.Transient` if you need something different:

```csharp
services.AddPipeline<OrderContext>(ServiceLifetime.Singleton)
    .Use<LoggingMiddleware>()
    .Handle<PlaceOrderHandler>();
```

The lifetime is applied both to the `Pipeline<T>` registration and to any middleware/handler classes `Use<...>()` / `Handle<...>()` register (if they are not already registered).

## 6. Run it

```csharp
var ctx = new OrderContext { OrderId = 42, UserId = 7 };
await pipeline.ExecuteAsync(ctx);
```

That's the whole API surface. For the full design rationale, behaviour contract, and the zero-allocation guarantees, see the [specification](SPEC.md).
