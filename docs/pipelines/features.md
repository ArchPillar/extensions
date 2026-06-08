# Features

Every feature of `ArchPillar.Extensions.Pipelines`, ordered from the most common
(composing a pipeline, writing middleware) to the most advanced (telemetry, the
allocation model). Each entry says what the feature is, when to reach for it, and
shows a compilable example. For the design contract behind these features, see the
[specification](internals/SPEC.md).

## Pipeline composition: middleware and a terminal handler

A `Pipeline<T>` is a single linear chain over a context type `T`. It is built from
zero or more **middlewares** (`IPipelineMiddleware<T>`) wrapping exactly one
**terminal handler** (`IPipelineHandler<T>`). The context `T` flows through every
step. Middlewares run in registration order, wrapping outward-in; the handler runs
last, after every middleware has called `next(...)`.

A middleware can run code before `next(...)`, after `next(...)`, both, or neither.
The handler is the innermost point of the chain — it produces the actual result of
the pipeline.

```csharp
using ArchPillar.Extensions.Pipelines;

public sealed class OrderContext
{
    public int OrderId { get; set; }
}

public sealed class LoggingMiddleware : IPipelineMiddleware<OrderContext>
{
    public async Task InvokeAsync(OrderContext context, PipelineDelegate<OrderContext> next, CancellationToken cancellationToken)
    {
        Console.WriteLine($"start {context.OrderId}");   // before
        await next(context, cancellationToken);          // continue the chain
        Console.WriteLine($"done  {context.OrderId}");   // after
    }
}

public sealed class PlaceOrderHandler : IPipelineHandler<OrderContext>
{
    public Task HandleAsync(OrderContext context, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"placed {context.OrderId}");
        return Task.CompletedTask;
    }
}
```

`Pipeline<T>` is `sealed` and exposes one method: `ExecuteAsync(context, cancellationToken)`.
A single instance is safe to invoke many times, including concurrently, as long as
the underlying middlewares and handler are themselves safe for concurrent use.

> The mental model is ASP.NET Core middleware. The one difference is that `next`
> takes the context and cancellation token explicitly — `next(context, cancellationToken)` —
> so the chain can be pre-built and the hot path stays allocation-free.

## Short-circuiting

A middleware short-circuits the pipeline by returning without calling `next(...)`.
Every middleware after it, and the handler, are skipped. Middlewares earlier in the
chain still run their "after" portion normally as control unwinds back out.

This is how guards, validation, and authorization stop work that should not proceed.

```csharp
public sealed class ValidationMiddleware : IPipelineMiddleware<OrderContext>
{
    public async Task InvokeAsync(OrderContext context, PipelineDelegate<OrderContext> next, CancellationToken cancellationToken)
    {
        if (context.OrderId <= 0)
        {
            return; // short-circuit: nothing downstream runs
        }

        await next(context, cancellationToken);
    }
}
```

## Error boundaries and cancellation

Exceptions thrown by the handler or any middleware propagate outward through the
chain to the caller of `ExecuteAsync`. A middleware can act as an error boundary by
wrapping `next(...)` in `try/catch`, letting it log, translate, or suppress failures
from downstream steps.

```csharp
public sealed class ErrorBoundaryMiddleware : IPipelineMiddleware<OrderContext>
{
    public async Task InvokeAsync(OrderContext context, PipelineDelegate<OrderContext> next, CancellationToken cancellationToken)
    {
        try
        {
            await next(context, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"order {context.OrderId} failed: {ex.Message}");
            throw; // or swallow, if this boundary owns the failure
        }
    }
}
```

The `CancellationToken` passed to `ExecuteAsync` is delivered to every middleware and
the handler. Middlewares are expected to honour it and pass it forward when they call
`next(context, cancellationToken)`.

## The `PipelineBuilder<T>` and the `Pipeline.For<T>()` entry point

When you are not using a DI container — tests, console tools, or any component that
constructs its own collaborators — build a pipeline by hand with the fluent builder.
`Pipeline.For<T>()` returns a `PipelineBuilder<T>`; chain `Use(...)` for each
middleware, `Handle(...)` for the terminal step, and `Build()` to compile.

```csharp
using ArchPillar.Extensions.Pipelines;

var pipeline = Pipeline
    .For<OrderContext>()
    .Use(new LoggingMiddleware())
    .Use(async (context, next, cancellationToken) =>   // lambda middleware
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

Middlewares execute in the order added, wrapping outward-in. `Build()` snapshots the
configured middlewares and compiles the chain; building without a handler throws
`InvalidOperationException`. The runnable
[`Pipelines.BuilderSample`](../../samples/Pipelines/Pipelines.BuilderSample/) shows this
end to end.

## Delegate-based middleware and handlers

You do not have to define a class for every step. `Use(...)` and `Handle(...)` accept
plain delegates, and the static `PipelineMiddleware.FromDelegate(...)` /
`PipelineHandler.FromDelegate(...)` factories wrap delegates into the interface types
directly — useful in tests and ad-hoc scripts.

`Use(...)` has two delegate shapes:

- `Func<T, PipelineDelegate<T>, CancellationToken, Task>` — the full form; you forward
  the cancellation token yourself.
- `Func<T, Func<T, Task>, Task>` — a simplified `next(context)` that forwards the
  pipeline's cancellation token automatically.

`Handle(...)` accepts `Action<T>`, `Func<T, Task>`, or
`Func<T, CancellationToken, Task>`.

```csharp
using ArchPillar.Extensions.Pipelines;

IPipelineMiddleware<OrderContext> guard = PipelineMiddleware.FromDelegate<OrderContext>(
    async (context, next, cancellationToken) =>
    {
        if (context.OrderId > 0)
        {
            await next(context, cancellationToken);
        }
    });

IPipelineHandler<OrderContext> handler = PipelineHandler.FromDelegate<OrderContext>(
    context => Console.WriteLine($"placed {context.OrderId}"));

var pipeline = Pipeline
    .For<OrderContext>()
    .Use(guard)
    .Handle(handler)
    .Build();

await pipeline.ExecuteAsync(new OrderContext { OrderId = 42 });
```

The `Action<T>` handler overload completes synchronously via `Task.CompletedTask`,
which keeps it on the allocation-free hot path.

## Dependency-injection integration

For host-based applications, register the pipeline in your `IServiceCollection` so
middlewares and the handler get full constructor injection. `Pipeline<T>` is resolved
by standard constructor injection — when the container builds it, it supplies the
registered `IPipelineHandler<T>` and the full `IEnumerable<IPipelineMiddleware<T>>`,
in registration order. There is no internal registry: every step is a plain
`ServiceDescriptor` the container and its tooling can introspect.

The DI surface is five extension methods on `IServiceCollection`.

```csharp
using ArchPillar.Extensions.Pipelines;
using Microsoft.Extensions.DependencyInjection;

// Register the pipeline with its required terminal handler.
services.AddPipeline<OrderContext, PlaceOrderHandler>();

// Contribute middlewares — independently of the pipeline registration.
services.AddPipelineMiddleware<OrderContext, LoggingMiddleware>();
services.AddPipelineMiddleware<OrderContext, ValidationMiddleware>();

// Resolve Pipeline<OrderContext> anywhere via constructor injection.
public sealed class OrderEndpoint(Pipeline<OrderContext> pipeline)
{
    public Task HandleAsync(OrderContext context, CancellationToken cancellationToken)
        => pipeline.ExecuteAsync(context, cancellationToken);
}
```

The runnable [`Pipelines.HostSample`](../../samples/Pipelines/Pipelines.HostSample/) wires
the same pipeline through a `Microsoft.Extensions.Hosting` app with `ILogger<T>`
injected into every middleware and the handler.

### `AddPipeline<T, THandler>` — register the pipeline and its handler

```csharp
services.AddPipeline<OrderContext, PlaceOrderHandler>(
    pipelineLifetime: ServiceLifetime.Scoped,
    handlerLifetime: ServiceLifetime.Scoped);
```

Registers both `Pipeline<T>` and the `IPipelineHandler<T>` it requires. A pipeline
without a handler is not valid — if you do not have a real handler yet, register a
dummy that throws. Both descriptors are written via `Replace`, so calling
`AddPipeline<T, THandler>()` again for the same `T` swaps both the pipeline lifetime
and the handler (last wins). `handlerLifetime` defaults to `pipelineLifetime`.

### `AddPipeline<T>(IPipelineHandler<T>)` — register with a handler instance

```csharp
services.AddPipeline<OrderContext>(
    PipelineHandler.FromDelegate<OrderContext>((context, cancellationToken) => Task.CompletedTask));
```

Registers `Pipeline<T>` with a pre-built handler instance — typically a delegate
wrapped by `PipelineHandler.FromDelegate(...)`. The instance is registered as a
singleton (stateless by construction) and is compatible with every pipeline lifetime,
so there is no separate handler-lifetime knob.

### `AddPipelineMiddleware<T, TMiddleware>` — contribute a middleware

```csharp
services.AddPipelineMiddleware<OrderContext, AuditMiddleware>(ServiceLifetime.Scoped);
```

Adds a middleware to `Pipeline<T>` without touching the pipeline registration. This is
how independent modules contribute middlewares to a pipeline owned by the application —
if the application never registers `Pipeline<T>`, the middleware is simply inert.
Registered via `TryAddEnumerable`, so calling it twice for the same `TMiddleware` is a
no-op. Middlewares execute in registration order.

### `ReplacePipelineHandler<T, THandler>` — swap the handler

```csharp
services.ReplacePipelineHandler<OrderContext, FakeOrderHandler>();
```

Removes every existing `IPipelineHandler<T>` descriptor and installs the replacement.
Useful in integration tests and for environment-specific overrides. To replace with an
instance directly, use `IServiceCollection.Replace`:

```csharp
services.Replace(ServiceDescriptor.Singleton<IPipelineHandler<OrderContext>>(
    PipelineHandler.FromDelegate<OrderContext>((context, cancellationToken) =>
    {
        return Task.CompletedTask;
    })));
```

### `AddPipelineTelemetry<T>` — wire the tracing middleware

```csharp
services.AddPipelineTelemetry<OrderContext>();
```

A shortcut for `AddPipelineMiddleware<T, ActivityMiddleware<T>>(...)` with the lifetime
defaulting to `Singleton` (the middleware is stateless). Requires
`T : IPipelineContext`. See [Distributed tracing](#distributed-tracing) below.

### Middleware ordering

Middlewares run in the order they are registered. Because they wrap outward-in, the
first registered is the outermost layer and the last registered sits closest to the
handler. Reordering registration changes behaviour — predictably.

```csharp
services.AddPipelineMiddleware<OrderContext, LoggingMiddleware>();      // outermost
services.AddPipelineMiddleware<OrderContext, ValidationMiddleware>();   // middle
services.AddPipelineMiddleware<OrderContext, AuthorizationMiddleware>(); // innermost (nearest handler)
```

### Lifetime rules

Each extension takes its own `ServiceLifetime`; the pipeline, each middleware, and the
handler are independent. The only registration-time check the library makes is one
obvious case: `AddPipeline<T, THandler>(pipelineLifetime: Singleton, handlerLifetime: Scoped)`
throws, because the two lifetimes are explicit in the same call and form a captive
dependency. Every other cross-registration check is delegated to the container — enable
`ValidateScopes` and `ValidateOnBuild` to catch them.

```csharp
using var provider = services.BuildServiceProvider(new ServiceProviderOptions
{
    ValidateScopes = true,
    ValidateOnBuild = true,
});
```

## Distributed tracing

The library ships a built-in distributed-tracing middleware, `ActivityMiddleware<T>`,
plus an optional contract, `IPipelineContext`, that a context opts into when it wants
telemetry. Together they wrap each pipeline execution in a
`System.Diagnostics.Activity` that flows to any OpenTelemetry exporter or raw
`ActivityListener` — with no framework lock-in.

### `IPipelineContext`

Implement this interface on your context type to participate. Only `OperationName` is
required; `ActivityKind`, `ParentContext`, and `EnrichActivity` all have default
interface implementations, so the minimum valid implementation is a single property.

```csharp
using System.Diagnostics;
using ArchPillar.Extensions.Pipelines;

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

- **`OperationName`** — required. The activity display name; typically a short
  hierarchical string (`"Orders.Place"`, `"Inventory.Reserve"`).
- **`ActivityKind`** — default `Internal`. Override to `Server` for inbound request
  handlers, `Consumer` for queue handlers, `Producer` for outbound-message pipelines,
  `Client` for outbound-call pipelines.
- **`ParentContext`** — default `default(ActivityContext)`, which falls back to
  `Activity.Current`. Override to inject a remote parent parsed from a `traceparent`
  header.
- **`EnrichActivity`** — default no-op. Called once, immediately after the activity is
  started and before `next(...)`. Add context-specific tags, events, or links here.

### `ActivityMiddleware<T>`

A drop-in middleware (constrained to `T : class, IPipelineContext`) that starts an
activity via the library-owned `ActivitySource`, calls `EnrichActivity`, awaits the
downstream chain, and disposes the activity on exit. On exception it records an
`exception` event with OpenTelemetry-conventional tags (`exception.type`,
`exception.message`, `exception.stacktrace`, `exception.escaped`) and sets the activity
status to `Error` — then rethrows. It never swallows.

Register it with the dedicated shortcut:

```csharp
services.AddPipelineTelemetry<OrderContext>();
```

> Ordering matters. Register the telemetry middleware **first** if you want the
> activity to wrap the full pipeline (every other middleware plus the handler);
> register it **last** to measure only the handler.

### `PipelineActivitySource`

Subscribers reference `PipelineActivitySource.Name`
(`"ArchPillar.Extensions.Pipelines"`) to activate tracing.

```csharp
using System.Diagnostics;
using ArchPillar.Extensions.Pipelines;

// OpenTelemetry:
builder.Services.AddOpenTelemetry().WithTracing(b => b
    .AddSource(PipelineActivitySource.Name)
    .AddOtlpExporter());

// Or a raw ActivityListener:
using var listener = new ActivityListener
{
    ShouldListenTo = s => s.Name == PipelineActivitySource.Name,
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    ActivityStopped = a => Console.WriteLine($"{a.DisplayName}: {a.Duration}"),
};
ActivitySource.AddActivityListener(listener);
```

When no `ActivityListener` is subscribed, `StartActivity` returns `null` and the
middleware is a pass-through: no activity allocation, no enrichment callback, no
exception-tag construction.

## The zero-allocation synchronous hot path

The middleware-to-handler composition is built **once** in the `Pipeline<T>`
constructor: the constructor walks the middleware array from last to first, wrapping
each middleware's delegate around the previously-built downstream, and stores the
resulting `PipelineDelegate<T>`. Every subsequent `ExecuteAsync` is a single delegate
invocation — there is no per-call composition and no per-call closure allocation.

On the synchronous hot path — the handler returns a cached completed task (e.g.
`Task.CompletedTask`) and middlewares tail-call `next` without `async`/`await` —
executing the pipeline allocates **zero bytes per invocation**. This is verified by
unit tests using `GC.GetAllocatedBytesForCurrentThread()` over 1,000 invocations per
scenario.

```csharp
// Allocation-free: tail-call next, no async/await state machine.
public sealed class PassthroughMiddleware : IPipelineMiddleware<OrderContext>
{
    public Task InvokeAsync(OrderContext context, PipelineDelegate<OrderContext> next, CancellationToken cancellationToken = default)
        => next(context, cancellationToken);
}

// Allocation-free: synchronous completion via a cached Task.
public sealed class NoopHandler : IPipelineHandler<OrderContext>
{
    public Task HandleAsync(OrderContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
```

> Middlewares written with `async`/`await` may allocate a state-machine box on the heap
> when they actually yield — that is a property of the C# async machinery, not of
> `Pipeline<T>`. Write middlewares that tail-call `next` when allocation matters; accept
> the cost when it does not.

## Snapshot semantics and reuse

The middlewares enumeration is snapshotted into an array in the constructor. Mutating
the caller's list afterward does not affect the built pipeline — what you passed in is
what runs, frozen at construction. Combined with the pre-built delegate chain, this
makes a `Pipeline<T>` a safe, reusable, long-lived object: build it once, hold it,
invoke it as often as you like.

## Fail-fast construction

`Pipeline<T>` validates eagerly. A null handler or a null middleware entry throws from
the constructor; building a `PipelineBuilder<T>` without a handler throws
`InvalidOperationException` from `Build()`. Every message names the type
(`Pipeline<OrderContext>`) and what the caller did wrong. A pipeline with no handler is
a misconfiguration, not a silent no-op — see the [error philosophy](internals/SPEC.md#error-philosophy)
in the spec.
