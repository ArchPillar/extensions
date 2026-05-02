# ArchPillar.Extensions.Commands — Specification

## Goal

Provide an in-process command dispatcher that:

- Has a tiny, hard-to-misuse public surface.
- Builds on `ArchPillar.Extensions.Pipelines` for cross-cutting concerns instead of inventing a parallel "behaviors" mechanism.
- Returns a uniform `OperationResult` / `OperationResult<TResult>` from every dispatch (no fire-and-forget).
- Stays AOT/trim-safe and reflection-free at dispatch time.
- Supports validation, exceptions-as-results, telemetry, and optional batching out of the box.

Explicit non-goals:

- **No queries.** Read paths use `ArchPillar.Extensions.Mapper`.
- **No events.** Domain events use `ArchPillar.Extensions.EventBus`.
- **No remote/transport providers.** Commands are dispatched in-process.
- **No source generators (yet).** Handler discovery is intentionally manual via DI registrations. A future `ArchPillar.Extensions.Commands.Analyzers` package will emit static registration calls.

## Conceptual model

```
                                ┌──────────────────────────────────────┐
                                │ Pipeline<CommandContext>             │
                                │                                      │
   ICommandDispatcher ──► CommandContext  ──► [ ActivityMiddleware ]   │
                                │              ↓                       │
                                │            [ ExceptionMiddleware ]   │
                                │              ↓                       │
                                │            [ ValidationMiddleware ]  │
                                │              ↓                       │
                                │            [ user middlewares ]      │
                                │              ↓                       │
                                │            [ CommandRouter ]         │
                                └──────────────────┬───────────────────┘
                                                   │
                                                   ▼
                                  ICommandHandler<TCommand>[, TResult]
```

A single shared pipeline means cross-cutting middlewares (transactions, logging, authorization) are written once and apply to every command. Per-command targeting, when needed, happens via `if (ctx.Command is CreateOrder)` checks inside the middleware.

## Type tier

```csharp
public interface IRequest;                              // base marker (plumbing)
public interface ICommand : IRequest;                   // no result
public interface ICommand<out TResult> : IRequest;      // with result
```

Handlers mirror it:

```csharp
public interface IRequestHandler;                       // base marker (plumbing)

public interface ICommandHandler<in TCommand> : IRequestHandler
    where TCommand : ICommand
{
    Task ValidateAsync(TCommand command, IValidationContext context, CancellationToken cancellationToken)
        => Task.CompletedTask;                          // default no-op

    Task<OperationResult> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

public interface ICommandHandler<in TCommand, TResult> : IRequestHandler
    where TCommand : ICommand<TResult>
{
    Task ValidateAsync(TCommand command, IValidationContext context, CancellationToken cancellationToken)
        => Task.CompletedTask;

    Task<OperationResult<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken);
}
```

Optional batch handlers:

```csharp
public interface IBatchCommandHandler<in TCommand> : IRequestHandler
    where TCommand : ICommand
{
    Task<IReadOnlyList<OperationResult>> HandleBatchAsync(
        IReadOnlyList<TCommand> commands, CancellationToken cancellationToken);
}

public interface IBatchCommandHandler<in TCommand, TResult> : IRequestHandler
    where TCommand : ICommand<TResult>
{
    Task<IReadOnlyList<OperationResult<TResult>>> HandleBatchAsync(
        IReadOnlyList<TCommand> commands, CancellationToken cancellationToken);
}
```

## Validation

Validation is part of the handler interface so validation can load entities from storage and validate against persisted state. The default `ValidateAsync` is a no-op; the optional `CommandHandlerBase<TCommand[, TResult]>` makes it abstract to nudge users into a deliberate choice.

`IValidationContext` accumulates `OperationError`s; the included `ValidationMiddleware` calls `ValidateAsync` and short-circuits with `OperationStatus.UnprocessableEntity` when errors are present.

`ValidationExtensions` provides composable helpers (`NotEmpty`, `NotBlank`, `Range`, `MaxLength`, `MinLength`, `Matches`, `Must`). They return the context for chaining.

## Result transport and exceptions

`CommandContext.Result` (`OperationResult?`) is the slot middlewares populate. The router writes the handler's return value; `ExceptionMiddleware` writes an `OperationResult` synthesized from any thrown exception:

- `OperationException` (thrown via `throw OperationResult.NotFound(...)`) → its carried `Result`.
- `OperationCanceledException` → re-thrown.
- Any other exception → `OperationResult.Failed(ex, OperationStatus.InternalServerError)`.

The dispatcher reads the slot once the pipeline completes. If the slot is still `null` (programmer error — middleware skipped without producing a result), the dispatcher synthesizes a 500.

## Lazy routing

Each `AddCommandHandler<TCommand, THandler>` adds:
- A typed `ICommandHandler<TCommand>` (or with-result) registration.
- A singleton `CommandInvokerDescriptor` that captures `(commandType, validateDelegate, invokeDelegate)`. The delegates are static, capturing only the generic type parameters.

The `CommandInvokerRegistry` stores the `IEnumerable<CommandInvokerDescriptor>` and a `ConcurrentDictionary<Type, CommandInvokerDescriptor?>`. On first lookup of a command type, the registry scans the enumeration; subsequent lookups hit the cache. This keeps host startup time proportional to commands actually dispatched.

`AddBatchCommandHandler<TCommand, THandler>` adds a `BatchInvokerEntry` alongside its typed handler registration. The registry composes the entry into the descriptor on first lookup.

`ValidateCommandRegistrations()` (extension on `IServiceProvider`) forces eager population — opt-in startup validation.

## Default DI registrations

`AddCommands()` registers, in order:

| Lifetime | Registration |
| --- | --- |
| Singleton | `CommandsOptions` |
| Singleton | `CommandInvokerRegistry` |
| Scoped    | `ICommandDispatcher` → `CommandDispatcher` |
| Scoped    | `Pipeline<CommandContext>` (via `AddPipeline<...>()`) |
| Scoped    | `IPipelineHandler<CommandContext>` → `CommandRouter` |
| Singleton | `IPipelineMiddleware<CommandContext>` → `ActivityMiddleware<CommandContext>` |
| Singleton | `IPipelineMiddleware<CommandContext>` → `ExceptionMiddleware` |
| Scoped    | `IPipelineMiddleware<CommandContext>` → `ValidationMiddleware` |

User-added middlewares append to the chain. The order is significant: telemetry wraps everything; the exception middleware wraps validation and the user middlewares; validation runs before any user middleware so transactional concerns never start when validation fails.

## Telemetry

`CommandActivitySource.Name = "ArchPillar.Extensions.Commands"`. Activities are started by `ActivityMiddleware<CommandContext>` (from the Pipelines library) — `CommandContext` implements `IPipelineContext`. The activity gets `command.type` as a tag. When no listener is attached the middleware is a zero-allocation pass-through.

## Cancellation

`CancellationToken` flows through every middleware and into the handler. Cancellation exceptions are not converted to results — they propagate unchanged so the host can react.

## Error-handling rules

- A handler may `return OperationResult.Failed(...)` or `throw OperationResult.Failed(...)`. Both produce the same outcome to the caller.
- Throwing any non-`OperationException` exception produces `OperationResult.Failed(ex)` with status 500.
- A missing handler registration produces `OperationResult.Failed(...)` at dispatch time; opt into startup validation to surface this earlier.

## What this library deliberately does not do

- **No request/query types.** Adding `IQuery<TResult>` would invite the same library to grow a second pipeline and become MediatR.
- **No streams or generators.** Commands are unary. Streaming belongs in domain-specific code or in a Pulse channel.
- **No `Send` overload that ignores the result.** Every dispatch produces an outcome; ignoring it is the caller's choice.
