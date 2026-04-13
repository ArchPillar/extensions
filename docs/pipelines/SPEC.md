# ArchPillar.Extensions.Pipelines — Specification

`ArchPillar.Extensions.Pipelines` is a lightweight, DI-friendly, allocation-free async middleware pipeline for .NET. This document is the specification for the library's single public type: **`Pipeline<T>`**.

The library has one unifying rule: **it is self-contained and framework-independent** — no shared machinery with other libraries, no runtime reflection, no hidden coupling. The core package depends only on BCL types; the companion DI package depends only on `Microsoft.Extensions.DependencyInjection.Abstractions`.

---

## Overview

`Pipeline<T>` is a lightweight async middleware pipeline. A pipeline is composed of:

- Zero or more **middlewares** (`IPipelineMiddleware<T>`), each of which wraps the remainder of the chain.
- Exactly one **terminal handler** (`IPipelineHandler<T>`), which runs after every middleware has called `next(...)`.
- A shared **context** (`T`), passed through every step.

Middlewares are composed as nested lambdas, with the handler at the innermost point. Each middleware can run code before and/or after `next(...)`, or skip calling `next(...)` to short-circuit the rest of the chain.

## Goals

- **Framework-independent.** The core package depends only on BCL types (`Task`, `Func`, `IEnumerable<T>`, `CancellationToken`). It is not tied to ASP.NET Core or any other host.
- **DI-native.** Middlewares and handlers are classes with constructor-injected dependencies. `Pipeline<T>` is trivially resolvable from any DI container, and the companion `AddPipeline<T>()` extension for `Microsoft.Extensions.DependencyInjection` provides fluent type-based registration.
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

    public int MiddlewareCount { get; }

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
    public PipelineBuilder<T> Use(Func<T, PipelineDelegate<T>, Task> invoke);

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
    public static IPipelineMiddleware<T> FromDelegate<T>(Func<T, PipelineDelegate<T>, Task> invoke);
}
```

Wrap plain delegates as `IPipelineHandler<T>` / `IPipelineMiddleware<T>`. Useful in tests and ad-hoc scripts where defining a class is overkill.

---

## DI integration

Lives in the companion package `ArchPillar.Extensions.Pipelines.DependencyInjection`.

### `services.AddPipeline<T>()`

```csharp
public static PipelineServiceBuilder<T> AddPipeline<T>(
    this IServiceCollection services,
    ServiceLifetime lifetime = ServiceLifetime.Scoped);
```

Registers `Pipeline<T>` in the service collection and returns a builder for configuring its middlewares and handler.

Calling `AddPipeline<T>()` a second time for the same `T` throws `InvalidOperationException`. A context type can have exactly one pipeline per service collection.

### `PipelineServiceBuilder<T>`

```csharp
public sealed class PipelineServiceBuilder<T>
{
    public ServiceLifetime Lifetime { get; }
    public int MiddlewareCount { get; }

    public PipelineServiceBuilder<T> Use<TMiddleware>()
        where TMiddleware : class, IPipelineMiddleware<T>;

    public PipelineServiceBuilder<T> Handle<THandler>()
        where THandler : class, IPipelineHandler<T>;
}
```

`Use<TMiddleware>()` appends a middleware class to the pipeline. If the class isn't already registered in the service collection, it is registered by its concrete type at the configured lifetime.

`Handle<THandler>()` sets the terminal handler. Calling it twice throws `InvalidOperationException`.

### Isolation

Middlewares are registered by their **concrete type** (`TMiddleware`), not as `IPipelineMiddleware<T>`. This is a deliberate design choice:

- **Two pipelines for the same `T` stay isolated.** If one pipeline uses `LoggingMiddleware` and another uses `RetryMiddleware`, they do not see each other's steps.
- **Unrelated `IPipelineMiddleware<T>` registrations don't leak into the pipeline.** Code that registers `services.AddSingleton<IPipelineMiddleware<OrderContext>, AuditMiddleware>()` does **not** attach `AuditMiddleware` to any pipeline built via `AddPipeline<OrderContext>()`. The pipeline knows exactly which concrete types it was told about, and asks the container for those.
- **`sp.GetServices<IPipelineMiddleware<T>>()` is not polluted.** Consumers who do want the global service namespace for their own reasons can use it freely.

At resolve time, the pipeline factory walks its own list of registered concrete types in order and calls `serviceProvider.GetRequiredService(type)` for each one. Every middleware and the handler get full constructor injection from the container.

### Lifetime

The `ServiceLifetime` parameter of `AddPipeline<T>()` applies to:

- The `Pipeline<T>` registration itself.
- Any middleware/handler class that `Use<T>()` / `Handle<T>()` registers (via `TryAdd`, so existing registrations with a different lifetime are respected).

The default is `Scoped`, matching typical per-request usage.

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
