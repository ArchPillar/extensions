# Recommendations

Patterns and guidance for using ArchPillar.Extensions.Commands in production
applications, with a focus on ASP.NET Core Minimal APIs.

A complete, runnable companion to this document lives in
[`samples/Commands/Command.WebApiSample/`](../../samples/Commands/Command.WebApiSample/) —
a small Notes service with EF Core (SQLite in-memory), a `TransactionMiddleware`,
batching, and telemetry wired up. The patterns below are the same patterns that
sample uses.

## Use the dispatcher for writes only

Commands are the write side. Reads — list, detail, search, paginate — should
stay out of the command pipeline. They don't mutate state, they don't need a
transactional shell, and pushing them through validation + middleware
introduces overhead without buying anything.

```csharp
// GET /notes — straight to EF Core. No dispatcher.
group.MapGet("/", async (NotesDbContext context, CancellationToken cancellationToken) =>
{
    var notes = await context.Notes
        .OrderByDescending(note => note.CreatedAt)
        .Select(note => new NoteResponse(note.Id, note.Title, note.Body, note.IsArchived))
        .ToListAsync(cancellationToken);
    return Results.Ok(notes);
});

// POST /notes — through the dispatcher.
group.MapPost("/", async (CreateNote command, ICommandDispatcher dispatcher, CancellationToken cancellationToken) =>
{
    OperationResult<Guid> result = await dispatcher.SendAsync(command, cancellationToken);
    return result.IsSuccess
        ? Results.Created($"/notes/{result.Value}", new { id = result.Value })
        : result.ToProblemResult();
});
```

The split keeps the dispatcher's job clear: every command that flows through
it represents intent to change the world. Reads stay where they're cheap.

## Keep commands internal; map at the API boundary

Commands are the internal service contract. The HTTP wire shape is a separate
concern: it gets versioned for clients, accumulates serialization quirks, and
needs to evolve without dragging the domain along. Don't bind a command record
straight from the request body — bind a request DTO and map it into the
command inside the endpoint.

```csharp
// API layer — versioned with the wire contract.
public sealed record CreateNoteRequest(string? Title, string? Body);

// Domain layer — versioned with the service.
internal sealed record CreateNote(string Title, string Body) : ICommand<Guid>;

group.MapPost("/", async (
    CreateNoteRequest request,
    ICommandDispatcher dispatcher,
    CancellationToken cancellationToken) =>
{
    var command = new CreateNote(request.Title ?? string.Empty, request.Body ?? string.Empty);
    OperationResult<Guid> result = await dispatcher.SendAsync(command, cancellationToken);
    return result.IsSuccess
        ? Results.Created($"/notes/{result.Value}", new { id = result.Value })
        : result.ToProblemResult();
});
```

Three reasons the seam matters:

1. **Stability.** A REST client serializes against the request DTO. If a
   downstream domain refactor renames a command property or splits one command
   into two, only the mapper inside the endpoint changes.
2. **Reach.** The dispatcher is reachable from non-HTTP callers — background
   workers, scheduled jobs, internal services that hold an
   `ICommandDispatcher`. Tying the command to a wire DTO leaks JSON-shaped
   nullability, validation attributes, and binding annotations into a
   contract those callers shouldn't see.
3. **Identity safety.** When the URL carries identity (`PUT /notes/{id}`,
   `POST /notes/{id}/archive`), composing the command from the route value
   keeps the route authoritative and prevents a client spoofing the ID via
   the body.

```csharp
public sealed record UpdateNoteRequest(string? Title, string? Body);

group.MapPut("/{id:guid}", async (
    Guid id,
    UpdateNoteRequest request,
    ICommandDispatcher dispatcher,
    CancellationToken cancellationToken) =>
{
    var command = new UpdateNote(id, request.Title ?? string.Empty, request.Body ?? string.Empty);
    OperationResult result = await dispatcher.SendAsync(command, cancellationToken);
    return result.IsSuccess ? Results.NoContent() : result.ToProblemResult();
});
```

### Where validation lives

The command's `ValidateAsync` is the authoritative validator — it sees the
same persisted state the handler sees, runs inside the same transaction, and
applies whether the dispatch came from HTTP, a job, or a test. Don't move
domain rules out of it.

API-layer validation is still useful as a *front guard* — a wire-shape check
that short-circuits malformed requests before they cost a dispatch:

- A request DTO with `[Required]` / `[MaxLength]` attributes plus an endpoint
  filter (or a FluentValidation validator on the DTO) gives clients a 400 for
  obviously broken payloads.
- It does not replace the command validator — it complements it. The command
  validator is what runs in non-HTTP paths.

When the two would say the same thing (`NotBlank`, `MaxLength`), pick one
layer and own the rule there. Defer to the command validator unless the
front guard buys something the dispatcher can't.

## Authentication and authorization at the right layer

Two flavors of "auth" show up in a typical service. They live in different
places:

| Concern | Where it lives | How |
| --- | --- | --- |
| Is the request authenticated? Does the principal hold the right role/scope? | API layer | ASP.NET authentication middleware, `RequireAuthorization()`, `[Authorize]` |
| Does *this* user have permission to operate on *this* entity? | Command layer | `validation.Authorize(...)` / `validation.Authenticate(...)` inside `ValidateAsync` |

```csharp
// HTTP-level — protocol concern, settled before the dispatcher is touched.
group.MapPost("/{id:guid}/archive", ArchiveAsync)
     .RequireAuthorization("notes.write");

// Domain-level — runs inside the transaction, sees the entity.
public override async Task ValidateAsync(ArchiveNote command, IValidationContext validation, CancellationToken cancellationToken)
{
    var note = await context.Notes.FindAsync([command.Id], cancellationToken);
    validation.Exists(note);
    validation.Authorize(note is null || note.OwnerId == currentUser.Id, "Only the owner can archive a note.");
}
```

Keeping the split is what lets the same command work from a job runner that
has no HTTP principal: the API gate is gone, but the domain check still
fires because it's part of the command's own contract.

## Project failures to RFC 7807 with one helper

`OperationResult.Problem` is already shaped like `application/problem+json`, so
a one-liner extension is enough to produce a problem response. Keep it
narrow — handle only the failure case so endpoints retain control of the
success shape (`Created`, `NoContent`, `Ok` with a custom DTO).

```csharp
internal static class OperationResultExtensions
{
    public static IResult ToProblemResult(this OperationResult result)
        => Results.Json(result.Problem, statusCode: (int)result.Status);
}
```

If you want richer integration (Swagger schemas, content-negotiation), wire
`OperationProblem` into `ProblemDetails` directly — the field names already
match.

## Register a transaction middleware as your first user middleware

The router runs validation immediately before the handler, so any user-supplied
middleware that wraps the dispatch wraps both halves. A transaction middleware
gives you ACID per command and eliminates the TOCTOU window between the read
the validator does ("does this entity exist?") and the write the handler does
("update it").

```csharp
internal sealed class TransactionMiddleware(NotesDbContext context, ILogger<TransactionMiddleware> logger)
    : IPipelineMiddleware<CommandContext>
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

The trade-off: a transaction is opened even when validation fails. In
practice that cost is rounding error compared to the cost of a TOCTOU bug,
and `OperationException` short-circuits cleanly so the rollback path stays
fast.

## Validate against persisted state in `ValidateAsync`

The default validator implementations check shape (`NotBlank`, `MaxLength`,
`Range`). The interesting validators check state — and they should live in
`ValidateAsync`, not at the top of `HandleAsync`.

```csharp
public override async Task ValidateAsync(UpdateNote command, IValidationContext validation, CancellationToken cancellationToken)
{
    validation.NotBlank(command.Title)
              .MaxLength(command.Title, 120)
              .NotBlank(command.Body)
              .MaxLength(command.Body, 4_000);

    var note = await context.Notes.FindAsync([command.Id], cancellationToken);
    validation.Exists(note);
    validation.Conflict(note is null || !note.IsArchived, "Cannot update an archived note.");
}
```

Why `ValidateAsync` and not `HandleAsync`? Three reasons:

1. **All errors at once.** The validator accumulates every failure. The
   client gets one response describing every problem, not a stream of
   single-error round-trips.
2. **Status precedence is automatic.** A `forbidden` error wins over a
   `bad_request` even if both fire — the precedence rule handles it without
   code in the handler.
3. **Same transactional snapshot.** Because validation runs inside the
   router (and therefore inside the transaction middleware), the read and
   the write both see the same database snapshot. No race.

## Throw `OperationResult` from handlers when it reads better

Inside a handler, you can either return a failure or throw it. Both produce
the same outcome to the caller; pick whichever keeps the code linear.

```csharp
public override async Task<OperationResult> HandleAsync(ArchiveNote command, CancellationToken cancellationToken)
{
    var note = await context.Notes.FindAsync([command.Id], cancellationToken);
    EnsureFound(note);                                  // throws → ExceptionMiddleware catches it

    if (note.IsArchived) return Conflict("Already archived.");   // OperationFailure → result

    note.IsArchived = true;
    await context.SaveChangesAsync(cancellationToken);
    return NoContent();
}
```

`EnsureFound` is shorthand for `throw OperationResult.NotFound(...)`. Use it
when the missing-entity path would clutter the happy path; use
`return Conflict(...)` when the failure path is the same shape as success and
returning is clearer than throwing.

## Use batching for actual bulk operations

`SendBatchAsync<TCommand[, TResult]>` validates each command, forwards the
valid ones to a registered `IBatchCommandHandler<TCommand[, TResult]>`, and
stitches per-command results back into input order. Two cases call for it:

- **Bulk inserts.** A batch handler can do one `SaveChangesAsync` for the
  whole input instead of N round-trips.
- **External APIs that take arrays.** Mirror their shape so you don't pay
  the per-call overhead.

For "small N" cases, dispatching individual commands in a loop is fine and
keeps the handler simpler. Reach for batching when the per-command cost
actually matters.

## Validate handler registrations at startup

Off by default to keep development cycles fast. Turn it on once your service
has more than a handful of commands so a missing registration surfaces at
startup rather than on the first dispatch:

```csharp
WebApplication app = builder.Build();
app.Services.ValidateCommandRegistrations();   // throws on the first handler that can't resolve
await app.RunAsync();
```

The check creates a scope and resolves every registered handler from DI. If
they all resolve, the call returns and the host continues to start.

## Subscribe to telemetry from one source

Every dispatch produces an `Activity` on `CommandActivitySource.Name`
(`"ArchPillar.Extensions.Commands"`). When no listener is attached, the
middleware is a zero-allocation pass-through.

```csharp
builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing
    .AddSource(CommandActivitySource.Name)
    .AddOtlpExporter());
```

Each activity is named `Commands.<CommandTypeName>` and carries a
`command.type` tag with the full type name. Combine with the equivalent
`PipelineActivitySource` subscription if you also use bare pipelines for
non-command workflows.

## Don't reach into the result for happy-path values

Once you've checked `IsSuccess`, prefer `Unwrap()` (sync) or
`UnwrapAsync()` (extension on `Task<OperationResult<T>>`) over `result.Value`.
Both throw `OperationException` on failure, so leaving an unwrap at the
boundary keeps the type signature honest:

```csharp
// Endpoint: keep the result, project failures
public async Task<IResult> Create(CreateNote command, ICommandDispatcher dispatcher, CancellationToken cancellationToken)
{
    OperationResult<Guid> result = await dispatcher.SendAsync(command, cancellationToken);
    return result.IsSuccess
        ? Results.Created($"/notes/{result.Value}", null)
        : result.ToProblemResult();
}

// Internal call: the caller wants the value, failure is exceptional
Guid id = await dispatcher.SendAsync(new CreateNote(...)).UnwrapAsync();
```

`Unwrap` belongs at the place where the surrounding code can't meaningfully
react to a failure status. `IsSuccess`-branching belongs everywhere else.
