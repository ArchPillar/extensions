# ArchPillar.Extensions.Commands — Specification

## Goal

Provide an in-process command dispatcher that:

- Has a tiny, hard-to-misuse public surface.
- Builds on `ArchPillar.Extensions.Pipelines` for cross-cutting concerns instead of inventing a parallel "behaviors" mechanism.
- Returns a uniform `OperationResult` / `OperationResult<TResult>` from every dispatch — every command is awaited, never queued or discarded.
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
   ICommandDispatcher ──► CommandContext  ──► [ CommandActivityMiddleware ]│
                                │              ↓                       │
                                │            [ ExceptionMiddleware ]   │
                                │              ↓                       │
                                │            [ user middlewares ]      │
                                │              ↓ (e.g. transactions)   │
                                │            [ CommandRouter ]         │
                                │                ├─ ValidateAsync(...) │
                                │                └─ HandleAsync(...)   │
                                └──────────────────┬───────────────────┘
                                                   │
                                                   ▼
                                  ICommandHandler<TCommand>[, TResult]
```

A single shared pipeline means cross-cutting middlewares (transactions, logging, authorization) are written once and apply to every command. Per-command targeting, when needed, happens via `if (ctx.Command is CreateOrder)` checks inside the middleware.

**Validation runs inside the router**, not as a separate middleware. This is deliberate: validation that touches storage ("is this order still cancellable?") needs to read on the same transactional snapshot the handler will write against. By running validation as the first thing the terminal handler does, every user-added wrapping middleware (a transaction, a unit-of-work, a retry-with-backoff, a distributed lock) is in scope for both the validation read and the handler's write — no TOCTOU race between them.

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

`IValidationContext` accumulates `(field?, OperationError)` entries — each error carries its own `OperationStatus` (set by the validator) and structured `Extensions`. The **router** calls `ValidateAsync` before invoking the handler. When entries are present, the router converts them via `ValidationContextExtensions.ToFailureResult()` into an `OperationResult` whose `Problem.Errors` is keyed by field name (RFC 7807 `application/problem+json` shape) and whose `Status` follows precedence:

```
401 Unauthorized > 403 Forbidden > 404 NotFound > 409 Conflict > 412 PreconditionFailed > 400 BadRequest > 422 UnprocessableEntity
```

so an authorization failure wins over an input-shape failure. Top-level (non-field) errors promote their `Type`/`Detail`/`Extensions` onto `Problem.Title`/`Detail`/`Extensions`.

`ValidationExtensions` provides composable helpers, each carrying its own default status:

| Helper | Status | Field captured | Notes |
| --- | --- | --- | --- |
| `NotNull` / `NotEmpty` / `NotBlank` | `BadRequest` | yes (via `[CallerArgumentExpression]`) | shape |
| `Range` / `MaxLength` / `MinLength` / `Matches` | `BadRequest` | yes | shape; populates `Extensions` (`min`/`max`/`actual`/`pattern`/`length`) |
| `Exists<T>` | `NotFound` | yes | top-level |
| `Authenticate` | `Unauthorized` | — | top-level, captures rule expression |
| `Authorize` | `Forbidden` | — | top-level |
| `Conflict` | `Conflict` | — | top-level |
| `Must` | `BadRequest` | optional | escape hatch |
| `Require` | caller-supplied | optional | full escape hatch |

All helpers return `IValidationContext` for chaining. The first parameter (the value being validated) becomes the field name automatically via `[CallerArgumentExpression]`.

### Validation and transactions

Validation often needs to check persisted state ("is this order still in cancellable state?", "does this customer exist?"). Those reads must happen on the same transactional snapshot the handler writes against — otherwise:

- A row read at validation time can be mutated before the handler's write, producing a TOCTOU race that turns a clean `OperationStatus.Conflict` into a generic 500 from a constraint violation.
- Validation and the handler can read at different isolation levels, disagreeing about "the same operation."

The router runs validation immediately before the handler, so any user-supplied wrapping middleware (transactions, unit-of-work, retry, distributed locks) is in scope for both halves. The trade-off: when validation fails the transaction was opened pointlessly. In practice that cost is rounding error compared to the cost of a TOCTOU bug, and `OperationException` short-circuits cleanly so the rollback path is fast.

## Result transport and exceptions

`CommandContext.Result` (`OperationResult?`) is the slot middlewares populate. The router writes the handler's return value; `ExceptionMiddleware` writes an `OperationResult` synthesized from any thrown exception:

- `OperationException` (thrown via `throw OperationResult.NotFound(...)`) → its carried `Result` (with the `Problem` body intact).
- `OperationCanceledException` → re-thrown.
- Any other exception → `OperationResult.Failed(ex)` (status 500, `Exception` captured at the top level, `Problem.Detail = ex.Message`).

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
| Singleton | `CommandInvokerRegistry` |
| Scoped    | `ICommandDispatcher` → `CommandDispatcher` |
| Scoped    | `Pipeline<CommandContext>` (via `AddPipeline<...>()`) |
| Scoped    | `IPipelineHandler<CommandContext>` → `CommandRouter` |
| Singleton | `IPipelineMiddleware<CommandContext>` → `CommandActivityMiddleware` |
| Singleton | `IPipelineMiddleware<CommandContext>` → `ExceptionMiddleware` |

User-added middlewares append to the chain. The order is significant: telemetry wraps everything; the exception middleware catches throws from user middlewares, the router, validation, and the handler. Validation is **not** a middleware — it runs inside the router so user middlewares (transactions, locks, retry) wrap both validation and the handler.

## Telemetry

`CommandActivitySource.Name = "ArchPillar.Extensions.Commands"`. Activities are started by `CommandActivityMiddleware` on the Commands-owned `ActivitySource` so subscribers can opt in to command dispatches without also receiving every other pipeline's activities. The activity gets `command.type` as a tag. When no listener is attached the middleware is a zero-allocation pass-through.

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
