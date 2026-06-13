# Recommendations

Production patterns for `ArchPillar.Extensions.Pipelines`. Each section is a single
recommendation with a short rationale and a code example. The focus is the non-obvious
parts — middleware ordering, lifetimes, what runs synchronously, and the allocation
model — where correct usage is not self-evident from the
[getting-started walkthrough](getting-started.md).

Two runnable samples back these patterns: the
[`Pipelines.BuilderSample`](../../samples/Pipelines/Pipelines.BuilderSample/) (direct,
no DI) and the [`Pipelines.HostSample`](../../samples/Pipelines/Pipelines.HostSample/)
(host builder + `AddPipeline<T>()` with `ILogger<T>` injection).

## Keep middleware ordering explicit

Middlewares run in registration order, wrapping outward-in: the first registered is the
outermost layer, the last sits closest to the handler. The library never reorders them.
This means the order of your `AddPipelineMiddleware<T, …>()` calls *is* the behaviour —
treat it as code, not configuration noise.

Order from outermost (cross-cutting, runs first and last) to innermost (nearest the
handler):

```csharp
services.AddPipeline<OrderContext, PlaceOrderHandler>();

services.AddPipelineMiddleware<OrderContext, LoggingMiddleware>();       // outermost
services.AddPipelineMiddleware<OrderContext, ValidationMiddleware>();    // middle
services.AddPipelineMiddleware<OrderContext, AuthorizationMiddleware>(); // innermost
```

A guard that should stop work early (validation, authorization) belongs *outside* the
expensive middleware it protects, so the short-circuit skips the cost.

> Anti-pattern: scattering `AddPipelineMiddleware<T, …>()` calls across unrelated
> registration methods and assuming the order does not matter. Two modules that each
> contribute a middleware compose into the union — in the order the modules ran. If
> ordering is load-bearing, register the order-sensitive middlewares together in one
> place.

## Register telemetry first to trace the whole pipeline

`AddPipelineTelemetry<T>()` is just `AddPipelineMiddleware<T, ActivityMiddleware<T>>()`
with a `Singleton` default, so it obeys the same ordering rule. Register it **first**
when you want the activity to span every other middleware and the handler. Register it
**last** when you only want to time the handler.

```csharp
services.AddPipeline<OrderContext, PlaceOrderHandler>();
services.AddPipelineTelemetry<OrderContext>();                       // outermost: wraps everything below
services.AddPipelineMiddleware<OrderContext, ValidationMiddleware>();
services.AddPipelineMiddleware<OrderContext, AuthorizationMiddleware>();
```

The `Pipelines.HostSample` registers it first for exactly this reason — the printed
activity duration covers the full chain.

## Hold pipelines as long-lived instances

The middleware-to-handler chain is composed once in the constructor and then reused.
Rebuilding a `Pipeline<T>` per request throws that work away and re-snapshots the
middleware list every time. Build it once and hold it.

In a host, that means letting DI cache the registration; outside a host, build the
pipeline at startup and keep the instance:

```csharp
// No DI: build once, reuse for the lifetime of the component.
public sealed class OrderProcessor
{
    private readonly Pipeline<OrderContext> _pipeline;

    public OrderProcessor()
    {
        _pipeline = Pipeline
            .For<OrderContext>()
            .Use(new ValidationMiddleware())
            .Handle(new PlaceOrderHandler())
            .Build();
    }

    public Task ProcessAsync(OrderContext context, CancellationToken cancellationToken)
        => _pipeline.ExecuteAsync(context, cancellationToken);
}
```

A single instance is safe to invoke concurrently, provided the middlewares and handler
are themselves thread-safe.

> Anti-pattern: calling `Pipeline.For<T>()...Build()` inside a request handler. The
> chain compilation is cheap but not free, and doing it per call defeats the design.

## Match the pipeline lifetime to its steps

Each registration takes its own `ServiceLifetime`, and they are independent. The pipeline
captures its handler and middlewares by constructor injection, so the usual captive-
dependency rule applies: a longer-lived pipeline cannot safely hold a shorter-lived step.

The library rejects exactly one combination at registration time — a `Singleton`
pipeline with an explicitly `Scoped` handler in the same `AddPipeline<T, THandler>()`
call. Everything else is the container's job. Turn on validation so the rest surfaces at
startup, not on the first scoped resolve:

```csharp
using var provider = services.BuildServiceProvider(new ServiceProviderOptions
{
    ValidateScopes = true,
    ValidateOnBuild = true,
});
```

`Host.CreateApplicationBuilder` enables both in the Development environment, so a
misconfigured lifetime fails fast in local runs.

> Anti-pattern: registering a `Singleton` pipeline over `Scoped` middlewares that hold
> a `DbContext`. The container resolves the middleware once into the singleton and
> reuses that captured instance across requests — a classic captive dependency. Either
> make the pipeline `Scoped` or make the middleware stateless and `Singleton`.

## Tail-call `next` when allocation matters

The zero-allocation guarantee holds only on the synchronous path: the handler returns a
cached completed task and middlewares tail-call `next` without `async`/`await`. A
middleware written with `async`/`await` allocates a state-machine box on the heap when
it actually yields. That is the C# async machinery, not the pipeline.

For pass-through and synchronous middlewares on a hot path, skip `async`:

```csharp
// Allocation-free: returns the downstream task directly.
public sealed class CounterMiddleware(Counter counter) : IPipelineMiddleware<OrderContext>
{
    public Task InvokeAsync(OrderContext context, PipelineDelegate<OrderContext> next, CancellationToken cancellationToken = default)
    {
        counter.Increment();
        return next(context, cancellationToken);
    }
}
```

Reach for `async`/`await` freely when the middleware genuinely awaits I/O — the box is
unavoidable there and trivial next to the I/O cost. Reserve the tail-call style for
synchronous cross-cutting concerns where the allocation would otherwise be pure overhead.

> Anti-pattern: writing `async Task InvokeAsync(...) { await next(...); }` for a
> middleware that does no awaiting of its own. It adds a state machine for nothing —
> return `next(context, cancellationToken)` instead.

## Return a cached completed task from synchronous handlers

The handler is the innermost step and runs on every non-short-circuited invocation. A
handler that has no asynchronous work to do should return `Task.CompletedTask` rather
than being marked `async`, keeping it on the allocation-free path.

```csharp
public sealed class StampHandler : IPipelineHandler<OrderContext>
{
    public Task HandleAsync(OrderContext context, CancellationToken cancellationToken = default)
    {
        context.OrderId = Math.Abs(context.OrderId);
        return Task.CompletedTask;
    }
}
```

The `Action<T>` overload of `PipelineHandler.FromDelegate` and `PipelineBuilder<T>.Handle`
does this for you — it completes synchronously via `Task.CompletedTask`.

## Short-circuit in middleware; do not throw to control flow

To stop the pipeline for an expected condition — invalid input, a failed authorization
check — return from the middleware without calling `next(...)`. That skips every
downstream middleware and the handler while still letting outer middlewares run their
"after" portion. Reserve exceptions for genuinely exceptional failures.

```csharp
public sealed class AuthorizationMiddleware : IPipelineMiddleware<OrderContext>
{
    public async Task InvokeAsync(OrderContext context, PipelineDelegate<OrderContext> next, CancellationToken cancellationToken)
    {
        if (context.UserId <= 0)
        {
            return; // expected: anonymous user, stop here
        }

        context.Authorized = true;
        await next(context, cancellationToken);
    }
}
```

If a step needs to *report* why it stopped, carry that on the context (a `Result` or
status field) rather than throwing — downstream and outer middlewares can read it.

> Anti-pattern: `throw new ValidationException(...)` for a routine validation failure.
> Exceptions are for the unexpected; an unwound stack trace per rejected request is
> expensive and noisy. Short-circuit instead.

## Use an error-boundary middleware, not try/catch at the call site

Because exceptions propagate outward through the chain, the natural place to handle
them is an outer middleware wrapping `next(...)` in `try/catch`. It sees every failure
from every downstream step in one place, regardless of how many call sites invoke the
pipeline.

```csharp
public sealed class ErrorBoundaryMiddleware(ILogger<ErrorBoundaryMiddleware> logger)
    : IPipelineMiddleware<OrderContext>
{
    public async Task InvokeAsync(OrderContext context, PipelineDelegate<OrderContext> next, CancellationToken cancellationToken)
    {
        try
        {
            await next(context, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "order {OrderId} failed", context.OrderId);
            throw;
        }
    }
}
```

`ActivityMiddleware<T>` already does this for telemetry: it records the exception on the
activity and rethrows, never swallowing. An error-boundary middleware composes cleanly
with it.

## Let independent modules contribute middlewares

`AddPipelineMiddleware<T, TMiddleware>()` does not touch the `Pipeline<T>` registration —
it only adds an `IPipelineMiddleware<T>` descriptor via `TryAddEnumerable`. A library
module can therefore contribute a middleware to a pipeline the application owns, without
either side knowing about the other. The resulting pipeline is the union of every
contributed middleware, in registration order.

```csharp
// In a reusable module — contributes to whatever Pipeline<OrderContext> the app registers.
public static IServiceCollection AddOrderAuditing(this IServiceCollection services)
    => services.AddPipelineMiddleware<OrderContext, AuditMiddleware>();
```

Because it is `TryAddEnumerable`, registering the same middleware type twice is a no-op,
so a module is safe to call its own `Add…` more than once. If the application never
registers `Pipeline<OrderContext>`, the contributed middleware is simply inert — there
is no error, and nothing resolves it.

> Anti-pattern: having a module call `AddPipeline<T, THandler>()` to "make sure the
> pipeline exists". That `Replace`s the handler and steals ownership of the pipeline
> lifetime from the application. Modules contribute middlewares; the application owns the
> pipeline and its handler.

## Replace the handler in tests rather than rebuilding the container

`ReplacePipelineHandler<T, THandler>()` removes any existing handler registration and
installs a new one, leaving the middleware registrations intact. Use it to swap the
production handler for a fake in an integration test without reconstructing the whole
service collection.

```csharp
// Production wiring is already applied; swap only the terminal handler.
services.ReplacePipelineHandler<OrderContext, FakeOrderHandler>();
```

For a delegate-backed fake, replace with an instance directly:

```csharp
services.Replace(ServiceDescriptor.Singleton<IPipelineHandler<OrderContext>>(
    PipelineHandler.FromDelegate<OrderContext>((context, cancellationToken) =>
    {
        context.Authorized = true;
        return Task.CompletedTask;
    })));
```

This keeps the middleware chain under test exactly as it ships, changing only the
terminal step.

## Subscribe to telemetry from one source

Every traced execution produces an `Activity` on `PipelineActivitySource.Name`
(`"ArchPillar.Extensions.Pipelines"`). Subscribe once — from OpenTelemetry in
production, or a raw `ActivityListener` in tests and samples. When no listener is
attached, `ActivityMiddleware<T>` is a zero-allocation pass-through, so leaving the
middleware registered costs nothing in environments that do not trace.

```csharp
builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing
    .AddSource(PipelineActivitySource.Name)
    .AddOtlpExporter());
```

For cross-process correlation — a queue consumer receiving a `traceparent` header —
parse the remote parent and surface it through the context's `ParentContext` so the
activity links to the upstream trace:

```csharp
public sealed class OrderContext : IPipelineContext
{
    public ActivityContext ParentContextOverride { get; set; }

    public string OperationName => "Orders.Place";
    public ActivityKind ActivityKind => ActivityKind.Consumer;
    public ActivityContext ParentContext => ParentContextOverride;
}

ActivityContext.TryParse(traceparentHeader, null, out var parent);
await pipeline.ExecuteAsync(new OrderContext { ParentContextOverride = parent });
```
