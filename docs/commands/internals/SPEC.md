# ArchPillar.Extensions.Commands — Specification

## Goal

Provide an in-process command dispatcher that:

- Has a tiny, hard-to-misuse public surface.
- Builds on `ArchPillar.Extensions.Pipelines` for cross-cutting concerns instead of inventing a parallel mechanism.
- Returns a uniform `OperationResult` / `OperationResult<TResult>` from every dispatch — every command is awaited, never queued or discarded.
- Stays AOT/trim-safe and reflection-free at dispatch time.
- Supports validation, exceptions-as-results, telemetry, and optional batching out of the box.

Explicit non-goals:

- **No queries or events.** This is a write-only command dispatcher; reads and notifications are outside its scope.
- **No remote/transport providers.** Commands are dispatched in-process.
- **No source generators (yet).** Handler discovery is intentionally manual via DI registrations. A future `ArchPillar.Extensions.Commands.Analyzers` package will emit static registration calls.

> Despite the surface similarity, this is a focused command dispatcher — not a general-purpose request/notification mediator.

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

A single shared pipeline means cross-cutting middlewares (transactions, logging, authorization) are written once and apply to every command. Per-command targeting, when needed, happens via `if (context.Command is CreateOrder)` checks inside the middleware.

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

Optional batch handlers — separate from the single-command handler and self-contained: the batch handler owns both validation and processing for the batch.

```csharp
public interface IBatchCommandHandler<in TCommand> : IRequestHandler
    where TCommand : ICommand
{
    Task ValidateAsync(IReadOnlyList<TCommand> commands, IValidationContext validation, CancellationToken cancellationToken)
        => Task.CompletedTask;

    Task<OperationResult> HandleBatchAsync(IReadOnlyList<TCommand> commands, CancellationToken cancellationToken);
}

public interface IBatchCommandHandler<in TCommand, TResult> : IRequestHandler
    where TCommand : ICommand<TResult>
{
    Task ValidateAsync(IReadOnlyList<TCommand> commands, IValidationContext validation, CancellationToken cancellationToken)
        => Task.CompletedTask;

    Task<OperationResult<IReadOnlyList<TResult>>> HandleBatchAsync(IReadOnlyList<TCommand> commands, CancellationToken cancellationToken);
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
| `Exists<T>` | `NotFound` | no | top-level — emits a non-field error so `Problem.Title`/`Detail` carry the message |
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

## Batch dispatch

`SendBatchAsync<TCommand[, TResult]>(commands)` returns a single `OperationResult` (or `OperationResult<IReadOnlyList<TResult>>` for the result-bearing form). Either the whole batch ran and succeeded, or it produced a single failure that aborted it.

Every batch dispatch runs through `Pipeline<CommandContext>` exactly once — the dispatcher always constructs a `CommandContext` carrying the whole input array (`BatchCommands` populated, `Command` `null`). Wrapping middleware (transactions, retry, telemetry, exception) sees one outer pass covering the whole group regardless of how the items are processed; one transaction commits or rolls back the entire batch, one activity records `command.batch.size`, the exception middleware catches any throw from across it.

Inside the router, two paths are chosen by registration:

- **Batch handler registered.** The router calls the batch handler's `ValidateAsync(IReadOnlyList<TCommand>, IValidationContext, …)`, and on success its `HandleBatchAsync(IReadOnlyList<TCommand>, …)`. The single-command `ICommandHandler<TCommand>.ValidateAsync` is *never* consulted on this path — the batch handler owns the rules. The handler is responsible for any per-item shape checks (typically by walking the list with index-keyed validator calls — the same `IValidationContext` accumulator surfaces them as field-keyed entries in the resulting `Problem.Errors`). All-or-nothing: if any validation entry was added, the handler is not invoked; the router writes the validation `OperationFailure` to `context.Result`. On success, the handler's return value is what the dispatcher hands back to the caller.
- **No batch handler.** The router iterates the input list internally. For each item it calls the per-command `ICommandHandler<TCommand>.ValidateAsync` then `HandleAsync` to completion before moving on. The loop bails on the first failure (validation *or* handler) and writes it verbatim to `context.Result`. On full success the router stores the per-item `OperationResult`s on `context.BatchResults` and writes a generic `Ok` marker to `context.Result`; the typed dispatcher then composes the `IReadOnlyList<TResult>` success payload from those, while the no-result form just returns the `Ok`. Items already processed are not rolled back unless your wrapping middleware does it (e.g. a transaction middleware seeing the failed result).

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

`CommandActivitySource.Name = "ArchPillar.Extensions.Commands"`. Activities are started by `CommandActivityMiddleware` on the Commands-owned `ActivitySource` so subscribers can opt in to command dispatches without also receiving every other pipeline's activities. The activity always carries `command.type` and, in batch dispatches, `command.batch.size`. Failures are recorded back onto the activity: when the inner `ExceptionMiddleware` absorbs a throw or the router writes a failure result, the activity middleware reads `context.Result` after the inner chain returns and marks the activity as `Error` with the failure detail (plus a `command.status` tag). When no listener is attached the middleware is a zero-allocation pass-through.

## Cancellation

`CancellationToken` flows through every middleware and into the handler. Cancellation exceptions are not converted to results — they propagate unchanged so the host can react.

## Error-handling rules

- A handler may `return OperationResult.Failed(...)` or `throw OperationResult.Failed(...)`. Both produce the same outcome to the caller.
- Throwing any non-`OperationException` exception produces `OperationResult.Failed(ex)` with status 500.
- A missing handler registration produces `OperationResult.Failed(...)` at dispatch time; opt into startup validation to surface this earlier.

## What this library deliberately does not do

- **No request/query types.** This library is command-only by design; introducing query types would push it toward a generic request dispatcher with a much broader surface — an explicit non-goal.
- **No streams or generators.** Commands are unary. Streaming belongs in domain-specific code or in a Pulse channel.
- **No `Send` overload that ignores the result.** Every dispatch produces an outcome; ignoring it is the caller's choice.
