# ArchPillar.Extensions.Pipelines — Specification

`ArchPillar.Extensions.Pipelines` is a lightweight, DI-friendly, allocation-free async middleware pipeline for .NET. This document is the specification for the library's single public type: **`Pipeline<T>`**.

The library has one unifying rule: **it is self-contained and framework-independent** — no shared machinery with other libraries, no runtime reflection, no hidden coupling. The only runtime dependency is `Microsoft.Extensions.DependencyInjection.Abstractions` (referenced privately — consumers who do not use the `IServiceCollection` extensions never see it).

---

## Overview

`Pipeline<T>` is a lightweight async middleware pipeline. A pipeline is composed of:

- Zero or more **middlewares** (`IPipelineMiddleware<T>`), each of which wraps the remainder of the chain.
- Exactly one **terminal handler** (`IPipelineHandler<T>`), which runs after every middleware has called `next(...)`.
- A shared **context** (`T`), passed through every step.

Middlewares are composed as nested lambdas, with the handler at the innermost point. Each middleware can run code before and/or after `next(...)`, or skip calling `next(...)` to short-circuit the rest of the chain.

## Goals

- **Framework-independent.** The engine uses only BCL types (`Task`, `Func`, `IEnumerable<T>`, `CancellationToken`). It is not tied to ASP.NET Core or any other host.
- **Dependency-injection-native.** Middlewares and handlers are classes with constructor-injected dependencies. `Pipeline<T>` is trivially resolvable from any dependency injection container, and the built-in `AddPipeline<T, THandler>()` extension for `Microsoft.Extensions.DependencyInjection` provides fluent type-based registration.
- **Allocation-free on the hot path.** The delegate chain is pre-built once in the constructor; subsequent invocations are a single delegate call. Synchronous pipelines allocate zero bytes per invocation.
- **Traceable.** Middlewares and handlers are real named classes. `Go to Definition` / `Find All References` work on them exactly as they do on any other service.
- **Familiar mental model.** If you have written ASP.NET Core middleware before, you already know how to write `IPipelineMiddleware<T>`.

## Non-goals

- **No magic.** Nothing scans assemblies, nothing inspects attributes, nothing discovers types by convention. The set of middlewares in a pipeline is exactly the set you registered.
- **No reflection at runtime.** The only reflection-adjacent operation is `IServiceProvider.GetRequiredService(Type)` in the DI package, which is how any DI-resolved type is built.
- **No implicit ordering.** Middlewares run in the order you add them. If you change the order, the behaviour changes — predictably.
- **No branching pipelines.** `Pipeline<T>` is a single linear chain. If you need branches, compose two pipelines and dispatch between them yourself.

---

## Contracts

### `PipelineDelegate<T>`

```csharp
public delegate Task PipelineDelegate<in T>(T context, CancellationToken cancellationToken);
```

A pre-built delegate representing every remaining step of the pipeline. Middlewares receive it as `next` and invoke it with `await next(context, cancellationToken)` to continue the chain.

Contravariant in `T` (`in T`) for the same reason `Action<T>` is: a delegate that accepts a base type can be used where a delegate that accepts a derived type is required.

### `IPipelineHandler<T>`

```csharp
public interface IPipelineHandler<in T>
{
    Task HandleAsync(T context, CancellationToken cancellationToken = default);
}
```

The terminal step of a pipeline. Runs after every middleware has called `next(...)`.

Each pipeline has exactly one handler.

### `IPipelineMiddleware<T>`

```csharp
public interface IPipelineMiddleware<T>
{
    Task InvokeAsync(T context, PipelineDelegate<T> next, CancellationToken cancellationToken = default);
}
```

A step that wraps the rest of the pipeline. Implementations:

- Can run code **before** `next(...)` (pre-processing)
- Can run code **after** `next(...)` (post-processing)
- Can **short-circuit** by returning without calling `next(...)`
- Can **catch** exceptions with `try/catch await next(...)`

`IPipelineMiddleware<T>` is invariant in `T` because `next`'s `PipelineDelegate<T>` appears in contravariant position inside a contravariant parameter, which cancels out to invariance.

### `Pipeline<T>`

```csharp
public sealed class Pipeline<T>
{
    public Pipeline(IPipelineHandler<T> handler, IEnumerable<IPipelineMiddleware<T>> middlewares);

    public Task ExecuteAsync(T context, CancellationToken cancellationToken = default);
}
```

The pipeline itself. Holds a pre-built `PipelineDelegate<T>` representing the composed chain.

The constructor:

1. Snapshots the middlewares enumeration into an array (so mutating the caller's list afterward does not affect the built pipeline).
2. Validates that the handler and every middleware entry are non-null.
3. Walks the array from last to first, wrapping each middleware's delegate around the previously-built downstream, producing a single `PipelineDelegate<T>`.
4. Stores that delegate for every subsequent `ExecuteAsync` call.

`ExecuteAsync` is a single delegate invocation. On the synchronous hot path (handler returns a cached completed task, middlewares tail-call `next`), executing the pipeline allocates zero bytes.

### `Pipeline` (static entry point)

```csharp
public static class Pipeline
{
    public static PipelineBuilder<T> For<T>() => new();
}
```

Factory method that produces a fluent `PipelineBuilder<T>`. Used when building pipelines manually (no DI container).

### `PipelineBuilder<T>`

```csharp
public sealed class PipelineBuilder<T>
{
    public PipelineBuilder<T> Use(IPipelineMiddleware<T> middleware);
    public PipelineBuilder<T> Use(Func<T, PipelineDelegate<T>, CancellationToken, Task> invoke);
    public PipelineBuilder<T> Use(Func<T, Func<T, Task>, Task> invoke);

    public PipelineBuilder<T> Handle(IPipelineHandler<T> handler);
    public PipelineBuilder<T> Handle(Func<T, CancellationToken, Task> handle);
    public PipelineBuilder<T> Handle(Func<T, Task> handle);
    public PipelineBuilder<T> Handle(Action<T> handle);

    public Pipeline<T> Build();
}
```

Accumulates middlewares and a handler, then produces a `Pipeline<T>` on `Build()`. Building without a handler throws `InvalidOperationException`.

### `PipelineHandler` / `PipelineMiddleware` (delegate helpers)

```csharp
public static class PipelineHandler
{
    public static IPipelineHandler<T> FromDelegate<T>(Func<T, CancellationToken, Task> handle);
    public static IPipelineHandler<T> FromDelegate<T>(Func<T, Task> handle);
    public static IPipelineHandler<T> FromDelegate<T>(Action<T> handle);
}

public static class PipelineMiddleware
{
    public static IPipelineMiddleware<T> FromDelegate<T>(Func<T, PipelineDelegate<T>, CancellationToken, Task> invoke);
    public static IPipelineMiddleware<T> FromDelegate<T>(Func<T, Func<T, Task>, Task> invoke);
}
```

Wrap plain delegates as `IPipelineHandler<T>` / `IPipelineMiddleware<T>`. Useful in tests and ad-hoc scripts where defining a class is overkill.

---

## DI integration

Shipped in the same package as the engine. Depends privately on `Microsoft.Extensions.DependencyInjection.Abstractions` (consumers who only use `Pipeline<T>` directly never see the dependency). The public surface is five extension methods on `IServiceCollection`: two to register the pipeline (by handler type or handler instance), one to contribute a middleware, one to swap the handler, and one shortcut for wiring `ActivityMiddleware<T>`.

### `AddPipeline<T, THandler>`

```csharp
public static IServiceCollection AddPipeline<T, THandler>(
    this IServiceCollection services,
    ServiceLifetime pipelineLifetime = ServiceLifetime.Scoped,
    ServiceLifetime? handlerLifetime = null)
    where THandler : class, IPipelineHandler<T>;
```

Registers `Pipeline<T>` and its required terminal handler. A pipeline without a handler is not valid — if you don't have a real handler yet, register a dummy that throws.

Both descriptors are written via `Replace`, so calling `AddPipeline<T, THandler>()` again for the same `T` swaps both the pipeline lifetime and the handler (last wins). `ReplacePipelineHandler<T, …>()` is the more explicit form for replacing only the handler.

`handlerLifetime` defaults to `pipelineLifetime`.

### `AddPipeline<T>(IPipelineHandler<T>)`

```csharp
public static IServiceCollection AddPipeline<T>(
    this IServiceCollection services,
    IPipelineHandler<T> handlerInstance,
    ServiceLifetime pipelineLifetime = ServiceLifetime.Scoped);
```

Registers `Pipeline<T>` with a pre-built handler instance. The handler is registered as a singleton (stateless by construction) and is compatible with every pipeline lifetime, so no lifetime knob is needed for the handler itself.

For delegate-backed handlers, wrap the delegate with `PipelineHandler.FromDelegate(...)`:

```csharp
services.AddPipeline<OrderContext>(
    PipelineHandler.FromDelegate<OrderContext>((ctx, ct) => Task.CompletedTask));
```

### `AddPipelineMiddleware<T, TMiddleware>`

```csharp
public static IServiceCollection AddPipelineMiddleware<T, TMiddleware>(
    this IServiceCollection services,
    ServiceLifetime lifetime = ServiceLifetime.Scoped)
    where TMiddleware : class, IPipelineMiddleware<T>;
```

Adds a middleware to `Pipeline<T>` without touching the pipeline registration. Library modules use this to contribute middlewares to a pipeline owned by the application — if the application never registers `Pipeline<T>`, the middleware is simply inert, and resolving the pipeline is the caller's responsibility.

Written via `TryAddEnumerable`, so calling it twice for the same `TMiddleware` is a no-op. Middlewares execute in registration order.

### `ReplacePipelineHandler<T, THandler>`

```csharp
public static IServiceCollection ReplacePipelineHandler<T, THandler>(
    this IServiceCollection services,
    ServiceLifetime lifetime = ServiceLifetime.Scoped)
    where THandler : class, IPipelineHandler<T>;
```

Removes every existing `IPipelineHandler<T>` descriptor and installs the replacement. Useful in integration tests and for environment-specific overrides. To replace with an instance directly, call `services.Replace(ServiceDescriptor.Singleton<IPipelineHandler<T>>(instance))`.

### `AddPipelineTelemetry<T>`

```csharp
public static IServiceCollection AddPipelineTelemetry<T>(
    this IServiceCollection services,
    ServiceLifetime lifetime = ServiceLifetime.Singleton)
    where T : class, IPipelineContext;
```

Shortcut for `AddPipelineMiddleware<T, ActivityMiddleware<T>>(lifetime)` with a default lifetime of `Singleton` (the middleware is stateless). Like the general middleware registration, it uses `TryAddEnumerable`, so calling it twice is a no-op. Call it before other `AddPipelineMiddleware<T, …>()` registrations if you want the activity to wrap the full pipeline; call it last to measure only the handler.

### Composition via DI

The key property of this design is that **`Pipeline<T>` is resolved by DI using standard constructor injection**. When the container builds a `Pipeline<T>`, it invokes the class's public constructor:

```csharp
public Pipeline(IPipelineHandler<T> handler, IEnumerable<IPipelineMiddleware<T>> middlewares)
```

Both parameters are resolved from the container:

- `IPipelineHandler<T>` — the handler registered via `AddPipeline<T, THandler>()` or `ReplacePipelineHandler<T, THandler>()` (or by any other code that registers the interface).
- `IEnumerable<IPipelineMiddleware<T>>` — **every** middleware registered as `IPipelineMiddleware<T>` in the container, in registration order.

This has several consequences, all intentional:

- **Multiple modules can contribute middlewares to the same pipeline.** Unrelated `AddPipelineMiddleware<T, X>()` calls compose — the resulting pipeline contains the union.
- **External `services.AddScoped<IPipelineMiddleware<T>, X>()` registrations are respected** and show up in the pipeline. This is how you compose a pipeline from parts that don't know about each other.
- **No internal registry, no hidden list.** Everything that drives the pipeline's behaviour is a plain `ServiceDescriptor` that standard DI tooling can introspect.

### Lifetime rules

Each extension takes its own `ServiceLifetime`. The pipeline, each middleware, and the handler are independent. The only registration-time check the library does is a single obvious one: `AddPipeline<T, THandler>` rejects the case where both `pipelineLifetime` and the resolved handler lifetime are passed in the same call and form a captive dependency (`pipelineLifetime: Singleton`, `handlerLifetime: Scoped`).

All other cross-registration lifetime validation is delegated to the container. Enable `ValidateScopes` and `ValidateOnBuild` on `ServiceProviderOptions` to catch captive dependencies across middleware/handler registrations:

```csharp
using var provider = services.BuildServiceProvider(new ServiceProviderOptions
{
    ValidateScopes = true,
    ValidateOnBuild = true,
});
```

Scoped and transient pipelines accept any step lifetime. Singleton middlewares and handlers are always compatible with any pipeline lifetime.

---

## Telemetry

The library exposes a built-in distributed-tracing middleware —
`ActivityMiddleware<T>` — plus an optional contract,
`IPipelineContext`, that contexts opt into when they want telemetry.

### `IPipelineContext`

```csharp
public interface IPipelineContext
{
    string OperationName { get; }
    ActivityKind ActivityKind => ActivityKind.Internal;
    ActivityContext ParentContext => default;
    void EnrichActivity(Activity activity) { }
}
```

- **`OperationName`** — required. The display name used for the activity.
  Typically a short, hierarchical string (`"Orders.Place"`,
  `"Inventory.Reserve"`).
- **`ActivityKind`** — default `Internal`. Override to `Server` for inbound
  request handlers, `Consumer` for queue handlers, `Producer` for
  outbound-message pipelines, `Client` for outbound-call pipelines.
- **`ParentContext`** — default `default(ActivityContext)`, which falls back
  to `Activity.Current`. Override to inject a remote parent parsed from a
  `traceparent` header (queue messages, async continuations across
  process boundaries).
- **`EnrichActivity`** — default no-op. Called once, immediately after the
  activity is started, before `next(...)`. Add context-specific tags,
  events, or links here.

Only `OperationName` is required. Every other member has a default
interface implementation — contexts that don't need the override don't
write one.

### `ActivityMiddleware<T>`

```csharp
public sealed class ActivityMiddleware<T> : IPipelineMiddleware<T>
    where T : class, IPipelineContext;
```

A drop-in middleware that:

1. Starts an activity via the library-owned `ActivitySource`, passing
   `context.OperationName`, `context.ActivityKind`, and
   `context.ParentContext` to `StartActivity(...)`.
2. Calls `context.EnrichActivity(activity)` if the activity is non-null.
3. Awaits `next(context, cancellationToken)`.
4. On exception: records an `exception` event on the activity with
   OpenTelemetry-conventional tags (`exception.type`,
   `exception.message`, `exception.stacktrace`, `exception.escaped`) and
   sets the activity status to `Error`. The exception is rethrown — the
   middleware never swallows.
5. Disposes the activity on exit.

When no `ActivityListener` is subscribed, `StartActivity` returns `null`
and the middleware is a pass-through: no activity allocation, no
enrichment callback, no exception-tag construction.

### `PipelineActivitySource`

```csharp
public static class PipelineActivitySource
{
    public const string Name = "ArchPillar.Extensions.Pipelines";
}
```

The source name that subscribers reference. With OpenTelemetry:

```csharp
builder.Services.AddOpenTelemetry().WithTracing(b => b
    .AddSource(PipelineActivitySource.Name)
    .AddOtlpExporter());
```

With a raw `ActivityListener`:

```csharp
using var listener = new ActivityListener
{
    ShouldListenTo = s => s.Name == PipelineActivitySource.Name,
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    ActivityStopped = a => Console.WriteLine($"{a.DisplayName}: {a.Duration}"),
};
ActivitySource.AddActivityListener(listener);
```

### Registration

Use the dedicated shortcut:

```csharp
services.AddPipelineTelemetry<OrderContext>();
```

This is equivalent to
`AddPipelineMiddleware<OrderContext, ActivityMiddleware<OrderContext>>()`
but with the lifetime defaulting to `Singleton` (the middleware is
stateless). Order matters: register it first if you want the activity
to wrap the full pipeline (including other middlewares); register it
last if you want it to measure only the handler.

### What is deliberately not on the interface

- **No `Tags` property.** The enrichment callback covers the same ground
  with more flexibility (tags + events + links) and no allocation in the
  zero-tags case.
- **No `CorrelationId` / `TraceId` property.** `Activity.Current.TraceId`
  is the source of truth — asking the context to expose one invites
  divergence.

---

## Behaviour contract

These are the guarantees `Pipeline<T>` makes. Each is covered by a unit test.

### Execution order

- Middlewares run in the order given to the constructor (or in the order added to the builder).
- Each middleware wraps everything after it. The "before" portion runs outward-in; the "after" portion runs inward-out.
- The handler runs last, after every middleware has called `next(...)`.

### Short-circuit

- A middleware that returns without calling `next(...)` prevents every subsequent middleware and the handler from running.
- Middlewares earlier in the chain still see their "after" portion execute normally.

### Exceptions

- Exceptions thrown by the handler or any middleware propagate outward through the chain.
- A middleware can catch exceptions from downstream steps with `try/catch await next(...)`.
- Unhandled exceptions are observed by the caller of `ExecuteAsync`.

### Cancellation

- The `CancellationToken` passed to `ExecuteAsync` is delivered to every middleware and the handler.
- Middlewares are expected to honour it and pass it forward when they call `next(ctx, ct)`.

### Reuse and concurrency

- `Pipeline<T>` is safe to invoke many times on a single instance.
- Concurrent invocations are safe as long as the underlying middleware/handler implementations themselves are safe for concurrent use.
- The composed delegate chain is built once in the constructor; subsequent invocations do not rebuild it.

### Snapshot semantics

- The middlewares enumeration is snapshotted in the constructor. Mutating the caller's list afterward does not affect the built pipeline.

### Allocation

- On the synchronous hot path (handler returns a cached completed task, middlewares tail-call `next` without `async`/`await`), `ExecuteAsync` allocates zero bytes per call. This is verified by `PipelineAllocationTests` over 1,000 invocations per scenario using `GC.GetAllocatedBytesForCurrentThread()`.
- Middlewares written with `async`/`await` may allocate a state machine box on the heap when they actually yield. This is a property of the C# async machinery, not of `Pipeline<T>`. Write middlewares without `async`/`await` (tail-call `next`) when allocation matters, or accept the cost when it doesn't.

---

## Error philosophy

- **Fail fast, at construction time.** Null handlers, null middleware entries, and missing handlers in the builder all throw synchronously from the constructor or `Build()`.
- **Fail fast, with clear messages.** Every error message names the type (`Pipeline<OrderContext>`) and explains what the caller did wrong.
- **No silent defaults.** A pipeline with no handler is a misconfiguration, not a no-op.
