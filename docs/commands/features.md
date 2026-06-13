# Features of ArchPillar.Extensions.Commands

Every feature the library ships, ordered from the one you reach for first to the
most advanced. Each entry says what the feature does, when to use it, and shows a
compilable example. For the design contract behind these features, see
[`internals/SPEC.md`](./internals/SPEC.md).

## Command dispatch

The dispatcher is the single public entry point. You declare a write operation as
an `ICommand` (status only) or `ICommand<TResult>` (status plus a payload), then
hand an instance to `ICommandDispatcher.SendAsync`. Every dispatch returns an
`OperationResult` / `OperationResult<TResult>` — there is no fire-and-forget
overload, because every command represents intent to change the world and the
caller always observes whether it succeeded.

```csharp
using ArchPillar.Extensions.Commands;
using ArchPillar.Extensions.Operations;

public sealed record CreateOrder(string Customer, int Quantity) : ICommand<Guid>;
public sealed record CancelOrder(Guid OrderId)                  : ICommand;

public sealed class OrdersService(ICommandDispatcher dispatcher)
{
    public async Task<Guid?> PlaceAsync(string customer, int quantity, CancellationToken cancellationToken)
    {
        OperationResult<Guid> result = await dispatcher.SendAsync(
            new CreateOrder(customer, quantity), cancellationToken);
        return result.IsSuccess ? result.Value : null;
    }
}
```

`ICommand` is for commands with no payload to return; `ICommand<TResult>` for the
ones that produce a value (a new identifier, a projection of the written entity).
Internally both extend the `IRequest` marker, which is plumbing — you never
implement `IRequest` directly.

> Reads do not belong here. This is a write-only dispatcher: queries, list
> endpoints, and search stay out of the pipeline where they are cheap. See
> [`recommendations.md`](./recommendations.md) for the read/write split.

## Handlers

A command type is bound to exactly one handler. Implement
`ICommandHandler<TCommand>` for a no-result command or
`ICommandHandler<TCommand, TResult>` for a result-bearing one. The handler is
where the write happens; its return value becomes the dispatch outcome.

```csharp
using ArchPillar.Extensions.Commands;
using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Operations;

public sealed class CancelOrderHandler(OrderContext context) : ICommandHandler<CancelOrder>
{
    public async Task<OperationResult> HandleAsync(CancelOrder command, CancellationToken cancellationToken)
    {
        var order = await context.Orders.FindAsync([command.OrderId], cancellationToken);
        if (order is null)
        {
            return OperationResult.NotFound("Order not found.");
        }

        order.Cancel();
        await context.SaveChangesAsync(cancellationToken);
        return OperationResult.NoContent();
    }
}
```

Constructor dependencies are resolved from DI, so a handler injects whatever it
needs — a `DbContext`, a repository, the current-user accessor.

### Base classes

Implementing the interface directly works, but the optional
`CommandHandlerBase<TCommand>` / `CommandHandlerBase<TCommand, TResult>` base
classes remove the boilerplate. They ship status factories
(`Ok`, `Created`, `Accepted`, `NoContent`, `NotFound`, `Conflict`,
`Unauthorized`, `Forbidden`, `BadRequest`) and assert helpers (`EnsureFound`,
`Ensure`, `EnsureAuthorized`, `EnsureNoConflict`) in the spirit of ASP.NET Core's
`ControllerBase`. They also leave `ValidateAsync` abstract, so the subclass is
forced to make a deliberate choice about validation rather than silently
inheriting a no-op.

```csharp
public sealed class CreateOrderHandler(OrderContext context)
    : CommandHandlerBase<CreateOrder, Guid>
{
    public override Task ValidateAsync(CreateOrder command, IValidationContext validation, CancellationToken cancellationToken)
    {
        validation.NotEmpty(command.Customer)
                  .Range(command.Quantity, 1, 100);
        return Task.CompletedTask;
    }

    public override async Task<OperationResult<Guid>> HandleAsync(CreateOrder command, CancellationToken cancellationToken)
    {
        var customer = await context.Customers.FindAsync([command.Customer], cancellationToken);
        EnsureFound(customer, "Customer not found.");        // throws → caught by the exception middleware

        var order = new Order(customer, command.Quantity);
        context.Orders.Add(order);
        await context.SaveChangesAsync(cancellationToken);
        return Created(order.Id);                            // factory returns OperationResult<Guid>
    }
}
```

The failure factories (`NotFound`, `Conflict`, …) return an `OperationFailure`
that converts implicitly to the typed result, so you can `return Conflict(...)`
from a `Task<OperationResult<Guid>>` body. The `Ensure*` helpers instead throw an
`OperationException` carrying the same status — use them when the failure path
would otherwise clutter the happy path.

## Validation

Validation lives on the handler, not in a separate middleware, so a validator can
load entities from storage and check against persisted state ("is this order
still cancellable?"). `ValidateAsync` receives an `IValidationContext` that
accumulates `(field, OperationError)` entries; the router calls it immediately
before `HandleAsync`. When any entry was added, the handler is skipped and the
accumulated errors become an `OperationResult` whose `Problem.Errors` is keyed by
field name (RFC 7807 `application/problem+json` shape).

The `ValidationExtensions` helpers are composable — each returns the context so
checks chain — and each carries its own default `OperationStatus`:

| Helper | Default status | Captures field | Notes |
| --- | --- | --- | --- |
| `NotNull` / `NotEmpty` / `NotBlank` | `BadRequest` | yes | shape checks |
| `Range` / `MaxLength` / `MinLength` / `Matches` | `BadRequest` | yes | shape; populate `Extensions` (`min`/`max`/`actual`/`length`/`pattern`) |
| `Exists<T>` | `NotFound` | no (top-level) | emits a non-field error so `Problem.Title`/`Detail` carry the message |
| `Authenticate` | `Unauthorized` | — (top-level) | captures the rule expression |
| `Authorize` | `Forbidden` | — (top-level) | |
| `Conflict` | `Conflict` | — (top-level) | |
| `Must` | `BadRequest` | optional | escape hatch |
| `Require` | caller-supplied | optional | full escape hatch |

```csharp
public override async Task ValidateAsync(UpdateNote command, IValidationContext validation, CancellationToken cancellationToken)
{
    // Shape checks — field name auto-captured from the argument expression.
    validation.NotBlank(command.Title)
              .MaxLength(command.Title, 120)
              .NotBlank(command.Body);

    // State checks — read on the same snapshot the handler will write against.
    var note = await context.Notes.FindAsync([command.Id], cancellationToken);
    validation.Exists(note);
    validation.Conflict(note is null || !note.IsArchived, "Cannot update an archived note.");
}
```

### Automatic field-name capture

The first argument to each shape helper — the value being validated — becomes the
field name automatically. The helpers carry a
`[CallerArgumentExpression]` parameter, so `validation.NotEmpty(command.Customer)`
records the field as `command.Customer` without you typing it. Pass `field:`
explicitly only when you want a friendlier label, or when the expression would not
read well (for example, an index into a list — `field: $"commands[{i}].Customer"`).

```csharp
validation.NotEmpty(command.Customer);                       // field => "command.Customer"
validation.NotEmpty(command.Customer, field: "customer");    // field => "customer"
```

### Status precedence

Different errors can fire in one pass. The boundary picks the top-level status by
precedence, so an authorization failure wins over a shape failure:

```text
401 Unauthorized > 403 Forbidden > 404 NotFound > 409 Conflict > 412 PreconditionFailed > 400 BadRequest > 422 UnprocessableEntity
```

> Validation runs *inside the router*, immediately before the handler. That
> placement is deliberate: any user middleware that wraps the dispatch (a
> transaction, a unit-of-work, a distributed lock) wraps both the validation read
> and the handler write, so they see the same transactional snapshot and there is
> no TOCTOU window between "does this exist?" and "update it."

## Cross-cutting middleware

Cross-cutting concerns are middlewares on the shared `Pipeline<CommandContext>`,
the same `IPipelineMiddleware<T>` contract from
[`ArchPillar.Extensions.Pipelines`](../pipelines/) — there is no second mechanism
to learn. A middleware written once applies to every command. Register it after
`AddCommands()` and it appends to the chain, wrapping the router (and therefore
both validation and the handler).

```csharp
using ArchPillar.Extensions.Commands;
using ArchPillar.Extensions.Pipelines;

public sealed class TransactionMiddleware(OrderContext context) : IPipelineMiddleware<CommandContext>
{
    public async Task InvokeAsync(
        CommandContext commandContext,
        PipelineDelegate<CommandContext> next,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await next(commandContext, cancellationToken);
        if (commandContext.Result is { IsSuccess: true })
        {
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            await transaction.RollbackAsync(cancellationToken);
        }
    }
}

services.AddPipelineMiddleware<CommandContext, TransactionMiddleware>();
```

The same pattern covers logging, authorization, idempotency, retry, and
distributed locks. When a middleware needs to target one command type, branch on
`commandContext.CommandType` or `commandContext.Command is CreateOrder` rather
than registering a separate pipeline.

> Order matters. A transaction middleware should usually be your *first* user
> middleware so its transaction wraps validation as well as the handler — see
> [`recommendations.md`](./recommendations.md).

## Result transport and exceptions

A handler can either `return` a failure or `throw` it; both produce the same
outcome to the caller. The built-in `ExceptionMiddleware` (registered by
`AddCommands()`) turns a throw into an `OperationResult`:

- An `OperationException` — what `EnsureFound`, `Ensure`, and
  `throw OperationResult.NotFound(...)` raise — yields its carried result, with
  the `Problem` body intact.
- An `OperationCanceledException` is re-thrown unchanged so the host can react.
- Any other exception becomes `OperationResult.Failed(ex)` (status 500, the
  exception captured, `Problem.Detail` set to its message).

```csharp
public override async Task<OperationResult> HandleAsync(ArchiveNote command, CancellationToken cancellationToken)
{
    var note = await context.Notes.FindAsync([command.Id], cancellationToken);
    EnsureFound(note);                                       // throw path — middleware writes the 404

    if (note.IsArchived)
    {
        return Conflict("Already archived.");               // return path — same outcome
    }

    note.IsArchived = true;
    await context.SaveChangesAsync(cancellationToken);
    return NoContent();
}
```

The dispatcher reads `CommandContext.Result` once the pipeline completes. If the
slot is still `null` (a middleware short-circuited without producing a result),
the dispatcher synthesizes a 500 rather than returning nothing.

## Batch handling

When a write is genuinely bulk — a batch insert that should be one round-trip, or
a call to an external API that takes arrays — opt into batching with
`SendBatchAsync`. A batch is one operation: either the whole batch ran and
produced its results, or it produced a single failure that aborted it. The entire
batch flows through the pipeline as a **single dispatch**, so a transaction
middleware commits or rolls back the whole group atomically and the activity
records `command.batch.size` once.

There are two ways the router processes a batch, chosen by registration.

### With a registered batch handler

Register an `IBatchCommandHandler<TCommand>` (or `…<TCommand, TResult>`) and the
router routes batch dispatches to it. The batch handler is self-contained: it owns
both validation and processing for the whole list, and the per-command
`ICommandHandler<TCommand>` is *not* consulted on this path. Per-item shape checks
typically loop over the list with index-keyed field labels so each failure
surfaces as a field-keyed entry.

```csharp
using ArchPillar.Extensions.Commands;
using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Operations;

public sealed class CreateOrderBatchHandler(OrderContext context)
    : IBatchCommandHandler<CreateOrder, Guid>
{
    public Task ValidateAsync(
        IReadOnlyList<CreateOrder> commands, IValidationContext validation, CancellationToken cancellationToken)
    {
        for (var i = 0; i < commands.Count; i++)
        {
            validation
                .NotEmpty(commands[i].Customer, field: $"commands[{i}].Customer")
                .Range(commands[i].Quantity, 1, 100, field: $"commands[{i}].Quantity");
        }

        return Task.CompletedTask;
    }

    public async Task<OperationResult<IReadOnlyList<Guid>>> HandleBatchAsync(
        IReadOnlyList<CreateOrder> commands, CancellationToken cancellationToken)
    {
        var orders = commands.Select(c => new Order(c.Customer, c.Quantity)).ToArray();
        context.Orders.AddRange(orders);
        await context.SaveChangesAsync(cancellationToken);   // one round-trip for the whole batch
        return OperationResult.Ok<IReadOnlyList<Guid>>(orders.Select(o => o.Id).ToArray());
    }
}

services.AddCommandHandler<CreateOrder, Guid, CreateOrderHandler>();   // single handler required too
services.AddBatchCommandHandler<CreateOrder, Guid, CreateOrderBatchHandler>();
```

Validation is all-or-nothing: if any entry was added, `HandleBatchAsync` is not
invoked and the call returns the validation failure. On success the handler's
return value is what the caller sees.

```csharp
OperationResult<IReadOnlyList<Guid>> result = await dispatcher.SendBatchAsync<CreateOrder, Guid>(
    [new CreateOrder("alice", 2), new CreateOrder("bob", 5)], cancellationToken);
```

### Without a batch handler

If no batch handler is registered, `SendBatchAsync` still works: the router
iterates the input list internally, running the per-command
`ICommandHandler<TCommand>` for each item — validation, then handler — to
completion before moving on. The loop bails on the first failure (validation *or*
handler) and surfaces it verbatim. On full success the result-bearing form
composes the `IReadOnlyList<TResult>` payload from the per-item outcomes; the
no-result form returns success. Items already processed are not rolled back unless
your wrapping middleware does it.

```csharp
// No CreateOrderBatchHandler registered — the router fans out per item.
OperationResult<IReadOnlyList<Guid>> result = await dispatcher.SendBatchAsync<CreateOrder, Guid>(
    [new CreateOrder("alice", 2), new CreateOrder("bob", 5)], cancellationToken);
```

> For "small N" cases where you do not need batch atomicity, an explicit loop of
> `SendAsync` calls in your own code is just as clear and skips the batch path
> entirely.

## Telemetry

`AddCommands()` registers `CommandActivityMiddleware`, which starts an `Activity`
for every dispatch on the Commands-owned `ActivitySource`
(`CommandActivitySource.Name == "ArchPillar.Extensions.Commands"`). Because the
source is owned by this library, subscribers can opt into command dispatches
without also receiving every other pipeline's activities. Each activity is named
`Commands.<CommandTypeName>` and carries a `command.type` tag (plus
`command.batch.size` for batches). When the dispatch fails — the inner
`ExceptionMiddleware` absorbs a throw or the router writes a failure — the
activity is marked `Error` with the failure detail and a `command.status` tag.

```csharp
builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing
    .AddSource(CommandActivitySource.Name)
    .AddOtlpExporter());
```

> When no listener is attached the middleware is a zero-allocation pass-through,
> so leaving it registered in production costs nothing until something subscribes.

## Cancellation

A `CancellationToken` flows through every middleware and into the handler.
Cancellation is not converted into a result: an `OperationCanceledException`
propagates unchanged through the exception middleware so the host can observe the
cancellation rather than seeing a synthesized failure.

```csharp
public async Task<OperationResult> HandleAsync(CancelOrder command, CancellationToken cancellationToken)
{
    var order = await context.Orders.FindAsync([command.OrderId], cancellationToken);
    EnsureFound(order);
    order.Cancel();
    await context.SaveChangesAsync(cancellationToken);       // throws OperationCanceledException on cancel — propagates
    return NoContent();
}
```

## DI registration

`AddCommands()` registers the dispatcher, the shared `Pipeline<CommandContext>`
(via the Pipelines `AddPipeline<...>()` API), the `CommandRouter` terminal
handler, and the two built-in middlewares. Each handler is then registered
explicitly — there is no assembly scanning, no `MakeGenericMethod`, and no source
generator, which keeps the library AOT/trim-safe. Every
`AddCommandHandler<TCommand, THandler>` captures its generic type parameters at
the registration site in a static delegate.

```csharp
using ArchPillar.Extensions.Commands;
using Microsoft.Extensions.DependencyInjection;

services.AddCommands();
services.AddCommandHandler<CreateOrder, Guid, CreateOrderHandler>();
services.AddCommandHandler<CancelOrder, CancelOrderHandler>();
services.AddBatchCommandHandler<CreateOrder, Guid, CreateOrderBatchHandler>();   // optional
```

Handlers default to a `Scoped` lifetime; pass a `ServiceLifetime` to override.
Routing is lazy — a command type's descriptor is resolved on first dispatch and
then cached, so host startup stays proportional to commands actually used.

### Startup validation

Routing is lazy, so a missing or unresolvable handler surfaces on first dispatch
by default. To surface it at startup instead, call
`ValidateCommandRegistrations()` on the built provider. It creates a scope and
resolves every registered handler, throwing `InvalidOperationException` naming the
first command whose handler cannot be constructed.

```csharp
WebApplication app = builder.Build();
app.Services.ValidateCommandRegistrations();   // throws on the first handler that cannot resolve
await app.RunAsync();
```

Leave it off for fast development cycles; turn it on once a service has more than a
handful of commands.
