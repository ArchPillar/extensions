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
    public static IPipelineMiddleware<T> FromDelegate<T>(Func<T, Func<T, Task>, Task> invoke);
}
```

Wrap plain delegates as `IPipelineHandler<T>` / `IPipelineMiddleware<T>`. Useful in tests and ad-hoc scripts where defining a class is overkill.

---

## DI integration

Lives in the companion package `ArchPillar.Extensions.Pipelines.DependencyInjection`. The public surface is three extension methods on `IServiceCollection`: one to register the pipeline with its required handler, one to contribute a middleware, and one to swap the handler.

### `AddPipeline<T, THandler>`

```csharp
public static IServiceCollection AddPipeline<T, THandler>(
    this IServiceCollection services,
    ServiceLifetime pipelineLifetime = ServiceLifetime.Scoped,
    ServiceLifetime? handlerLifetime = null)
    where THandler : class, IPipelineHandler<T>;
```

Registers `Pipeline<T>` and its required terminal handler. A pipeline without a handler is not valid — if you don't have a real handler yet, register a dummy that throws.

Both descriptors are written via `TryAdd`, so calling `AddPipeline<T, THandler>()` a second time for the same `T` is a no-op (first handler wins). To swap the handler after the fact, use `ReplacePipelineHandler<T, ...>()`.

`handlerLifetime` defaults to `pipelineLifetime`.

#### Delegate overloads

For inline handlers (tests, adapters, trivial sinks):

```csharp
public static IServiceCollection AddPipeline<T>(
    this IServiceCollection services,
    Func<T, CancellationToken, Task> handler,
    ServiceLifetime pipelineLifetime = ServiceLifetime.Scoped);

public static IServiceCollection AddPipeline<T>(
    this IServiceCollection services,
    Func<T, Task> handler,
    ServiceLifetime pipelineLifetime = ServiceLifetime.Scoped);

public static IServiceCollection AddPipeline<T>(
    this IServiceCollection services,
    Action<T> handler,
    ServiceLifetime pipelineLifetime = ServiceLifetime.Scoped);
```

The delegate is wrapped via `PipelineHandler.FromDelegate(...)` and registered as a singleton `IPipelineHandler<T>` instance. Singletons are compatible with every pipeline lifetime, so no lifetime knob is needed for the handler itself.

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

public static IServiceCollection ReplacePipelineHandler<T>(
    this IServiceCollection services,
    Func<T, CancellationToken, Task> handler);

public static IServiceCollection ReplacePipelineHandler<T>(
    this IServiceCollection services,
    Func<T, Task> handler);

public static IServiceCollection ReplacePipelineHandler<T>(
    this IServiceCollection services,
    Action<T> handler);
```

Removes every existing `IPipelineHandler<T>` descriptor and installs the replacement. Useful in integration tests and for environment-specific overrides. Delegate overloads register the replacement as a singleton instance.

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

Each extension takes its own `ServiceLifetime`. The pipeline, each middleware, and the handler are independent. The only combination rejected at registration time is a **singleton `Pipeline<T>` with a scoped middleware or handler** — the classic captive-dependency bug. The check fires in both directions:

| When you call                                               | It throws if                                                         |
| ----------------------------------------------------------- | -------------------------------------------------------------------- |
| `AddPipeline<T, THandler>(Singleton, Scoped)`               | The handler lifetime is scoped                                       |
| `AddPipeline<T, _>(Singleton, ...)`                         | A scoped `IPipelineMiddleware<T>` or `IPipelineHandler<T>` already exists |
| `AddPipelineMiddleware<T, _>(Scoped)`                       | `Pipeline<T>` is already registered as singleton                     |
| `ReplacePipelineHandler<T, _>(Scoped)`                      | `Pipeline<T>` is already registered as singleton                     |

Scoped and transient pipelines accept any step lifetime. Singleton middlewares and handlers are always compatible with any pipeline lifetime.

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
