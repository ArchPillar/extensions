# Getting Started — `Pipeline<T>`

This walks you through installing `ArchPillar.Extensions.Pipelines`, writing your first pipeline, and wiring it up both manually and through `Microsoft.Extensions.DependencyInjection`.

## 1. Install

The library ships as a single NuGet package:

```bash
dotnet add package ArchPillar.Extensions.Pipelines
```

Depends on `Microsoft.Extensions.DependencyInjection.Abstractions` and nothing else. You can use `Pipeline<T>` directly via `Pipeline.For<T>()...Build()` without touching a DI container, or register it through `services.AddPipeline<T, THandler>()` when you do.

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
using ArchPillar.Extensions.Pipelines;

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
    public async Task InvokeAsync(OrderContext context, PipelineDelegate<OrderContext> next, CancellationToken cancellationToken)
    {
        if (context.OrderId <= 0)
        {
            Console.WriteLine("invalid order");
            return; // short-circuit
        }

        await next(context, cancellationToken);
    }
}
```

### Why does `next` take `(context, cancellationToken)` explicitly?

Unlike ASP.NET Core's `Func<Task> next`, `Pipeline<T>`'s `PipelineDelegate<T> next` is a pre-built delegate that takes the context and cancellation token as arguments. The chain is composed **once** in the `Pipeline<T>` constructor; invoking `next(context, cancellationToken)` is a single delegate call, not a per-invocation allocation. This keeps the synchronous hot path allocation-free.

## 5a. Build a pipeline without DI

For tests, tools, or apps that manage their own dependencies, use the fluent `Pipeline.For<T>()` builder:

```csharp
var pipeline = Pipeline
    .For<OrderContext>()
    .Use(new ValidationMiddleware())
    .Use(async (context, next, cancellationToken) =>   // lambda middleware
    {
        Console.WriteLine($"start {context.OrderId}");
        await next(context, cancellationToken);
        Console.WriteLine($"done  {context.OrderId}");
    })
    .Handle(new PlaceOrderHandler())
    .Build();

await pipeline.ExecuteAsync(new OrderContext { OrderId = 42 });
```

Middlewares execute in the order you added them, wrapping outward → inward. `Handle(...)` sets the terminal step and can also take a plain delegate (`Action<T>`, `Func<T, Task>`, or `Func<T, CancellationToken, Task>`).

## 5b. Build a pipeline with DI

For host-based applications (`Microsoft.Extensions.Hosting`, ASP.NET Core, etc.), register the pipeline in your `IServiceCollection` so every middleware and the handler get full constructor injection. The DI integration is part of the core package and exposes four extension methods:

```csharp
using ArchPillar.Extensions.Pipelines;
using Microsoft.Extensions.DependencyInjection;

// 1) Register the pipeline with its required terminal handler.
services.AddPipeline<OrderContext, PlaceOrderHandler>();

// 2) Contribute middlewares — either from application startup or from
//    independent library modules. AddPipelineMiddleware<T, M>() is a
//    standalone extension: it does not touch the Pipeline<T> registration,
//    so modules can contribute middlewares to a pipeline they don't own.
services.AddPipelineMiddleware<OrderContext, LoggingMiddleware>();
services.AddPipelineMiddleware<OrderContext, ValidationMiddleware>();
services.AddPipelineMiddleware<OrderContext, AuthorizationMiddleware>();
```

Now any component can take `Pipeline<OrderContext>` as a constructor dependency:

```csharp
public sealed class OrderEndpoint(Pipeline<OrderContext> pipeline)
{
    public Task HandleAsync(OrderContext context, CancellationToken cancellationToken)
        => pipeline.ExecuteAsync(context, cancellationToken);
}
```

The middlewares are registered in the DI container as `IPipelineMiddleware<OrderContext>` via `TryAddEnumerable`, and the handler as `IPipelineHandler<OrderContext>` via `Replace`. `Pipeline<OrderContext>` is then composed from whichever implementations are registered — see the [SPEC](SPEC.md#composition-via-di) for details.

### Handler as an instance

When a class handler is overkill (tests, adapters, trivial sinks), pass a pre-built `IPipelineHandler<T>` — typically one wrapped from a delegate:

```csharp
services.AddPipeline<OrderContext>(
    PipelineHandler.FromDelegate<OrderContext>((context, cancellationToken) => SomeTask(context, cancellationToken)));
```

`PipelineHandler.FromDelegate` has overloads for `Func<T, CancellationToken, Task>`, `Func<T, Task>`, and `Action<T>`. The instance overload registers the handler as a singleton and is always compatible with any pipeline lifetime.

### Replacing the handler in tests

`ReplacePipelineHandler<T, THandler>()` removes any existing handler registration and installs a new one. Use this in integration tests to swap out the production handler without rebuilding the service collection:

```csharp
services.ReplacePipelineHandler<OrderContext, FakeOrderHandler>();
```

To replace with a delegate-backed handler directly, use `IServiceCollection.Replace` with `PipelineHandler.FromDelegate(...)`:

```csharp
services.Replace(ServiceDescriptor.Singleton<IPipelineHandler<OrderContext>>(
    PipelineHandler.FromDelegate<OrderContext>((context, cancellationToken) =>
    {
        context.Authorized = true;
        return Task.CompletedTask;
    })));
```

### Lifetimes

Each extension takes its own `ServiceLifetime`. The pipeline, each middleware, and the handler are independent:

```csharp
services.AddPipeline<OrderContext, PlaceOrderHandler>(
    pipelineLifetime: ServiceLifetime.Scoped,
    handlerLifetime: ServiceLifetime.Scoped);

services.AddPipelineMiddleware<OrderContext, LoggingMiddleware>(ServiceLifetime.Singleton);
services.AddPipelineMiddleware<OrderContext, AuditMiddleware>(ServiceLifetime.Scoped);
```

The library only rejects one obvious combination at registration time: `AddPipeline<T, THandler>(pipelineLifetime: Singleton, handlerLifetime: Scoped)` throws, because the two lifetimes are explicit in the same call. Every other captive-dependency check is delegated to the container — enable `ValidateScopes` and `ValidateOnBuild` on `ServiceProviderOptions` (or use `Host.CreateApplicationBuilder`, which turns them on in Development) to catch the rest.

## 5c. Opt into distributed tracing

`ActivityMiddleware<T>` wraps the pipeline in a `System.Diagnostics.Activity`. It works on any `T` that implements `IPipelineContext`:

```csharp
using System.Diagnostics;

public sealed class OrderContext : IPipelineContext
{
    public int OrderId { get; set; }
    public int UserId { get; set; }

    public string OperationName => "Orders.Place";

    public void EnrichActivity(Activity activity)
    {
        activity.SetTag("order.id", OrderId);
        activity.SetTag("user.id", UserId);
    }
}
```

Only `OperationName` is required. Override `ActivityKind` (default `Internal`), `ParentContext` (default: inherit `Activity.Current`), or `EnrichActivity` as needed. Register the middleware with the dedicated shortcut:

```csharp
services.AddPipelineTelemetry<OrderContext>();
```

(This is equivalent to `AddPipelineMiddleware<OrderContext, ActivityMiddleware<OrderContext>>()` but with a default lifetime of `Singleton` — the middleware is stateless.)

Activities flow to whatever subscribes to the library's `ActivitySource`:

```csharp
// OpenTelemetry:
builder.Services.AddOpenTelemetry().WithTracing(b => b
    .AddSource(PipelineActivitySource.Name)
    .AddOtlpExporter());

// Raw ActivityListener:
using var listener = new ActivityListener
{
    ShouldListenTo = s => s.Name == PipelineActivitySource.Name,
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    ActivityStopped = a => Console.WriteLine($"{a.DisplayName}: {a.Duration}"),
};
ActivitySource.AddActivityListener(listener);
```

**Injecting a remote parent** — for a queue consumer receiving a `traceparent` header:

```csharp
ActivityContext.TryParse(traceparentHeader, null, out var parent);
await pipeline.ExecuteAsync(new OrderContext
{
    ParentContextOverride = parent,  // expose via a ParentContext property
    OrderId = ...,
});
```

When no subscriber is attached, `ActivityMiddleware<T>` is a pass-through — no allocation, no activity overhead.

## 6. Run it

```csharp
var context = new OrderContext { OrderId = 42, UserId = 7 };
await pipeline.ExecuteAsync(context);
```

That's the whole API surface. For the full design rationale, behaviour contract, and the zero-allocation guarantees, see the [specification](SPEC.md).
