# Commands — roadmap

The plan for taking `ArchPillar.Extensions.Commands` from an in-process
dispatcher to one that also covers choreography and microservices. Companion
to [REVIEW.md](REVIEW.md), which is the point-in-time assessment this plan
answers.

Phase 1 below is a **platform prerequisite rather than Commands work** — the
type registry serves models, commands, and events alike. It is planned here
because the Commands work is what forces it, but it gets its own spec under
`docs/primitives/` when it is built.

## The through-line

Three deployment shapes, one command contract:

| Shape | What it means here |
| --- | --- |
| Modular monolith | every command resolves to a local handler; middlewares give it ACID |
| Choreography | a known peer owns a command; the local host routes to it over a direct channel |
| Microservices | the owner is not known up front; it is discovered by class id |

The promise is that **the handler code, the call site, and the returned
`OperationResult` are identical in all three** — only registration changes.
Every decision below is measured against that.

The proof obligation is a single sample with two hosts and one shared contracts
assembly, running in all three configurations with no difference outside
`Program.cs`. If that sample needs an `if` anywhere else, the design is wrong.

---

## Phase A — clear the ground

The review's blocking findings gate the feature work; they are cheaper to fix
before three features are built on top of them than after.

1. **Delete the no-batch-handler iterate fallback** (REVIEW A1). Removes
   `CommandContext.BatchResults`, the `Ok()` marker, `ComposePerItem`,
   `ProducesResult`, and the unexpected-shape 500. Decide separately whether
   the whole batch tier goes in favour of wrapper commands — it removes a
   batch envelope from the wire protocol and the discovery catalogue (B5).
2. **Fix missing-handler semantics** (A2): the router writes the failure
   directly rather than throwing into `ExceptionMiddleware`; correct the false
   startup-validation claim in the SPEC; fail fast on batch-only registration.
3. **Carry `Type? ResultType` on the descriptor** in place of
   `ProducesResult` (B2) — remoting cannot deserialise a payload without it.
4. **Open `ActivityKind` and `ParentContext` on `CommandContext`** (B4) so a
   receiving host can start a `Server` span parented to the caller's
   `traceparent`. The wire format has to carry trace context from v1.
5. **Write down the nesting contract** (B1): a dispatch does not create a child
   DI scope, nested commands share the outer command's scoped services, and
   the documented `TransactionMiddleware` is replaced by the real one from
   Phase 2.
6. **Close the test gaps** (A10): re-entrant dispatch, cancellation
   propagation, registration traps, and the typed batch path if it survives.

---

## Blocked on — the module system

Registration needs a home before anything can register into it, so `IModule`
and its registration helpers gate Phase 1. **Designing that is out of scope
here** and is tracked separately; this plan only records the dependency.

What Phase 1 needs from it: a per-module place to contribute type
registrations, and a post-build initialization hook to run the registry's
database reconciliation before the host serves traffic.

---

## Phase 1 — the type registry (in Primitives)

A **universal** whitelisting subsystem mapping a stable class id to a CLR type,
with optional metadata: every model, every command, every event. Nothing about
it is command-specific — an outbox, an audit trail, or a saga store needs the
same lookup — which is why it belongs in Primitives rather than in Commands.

It is **strictly separate from `CommandInvokerRegistry`**: the type registry
knows what a command *is*, the invoker registry knows who *handles* it. Keeping
them apart is what makes the three-way routing distinction below expressible.

### Surface

An interface opts its implementations into whitelisting:

```csharp
[RequiresClassId]
public interface ICommand : ICommandBase;

[ClassId("0189c7f4-3b2a-7c1e-9d55-2f8a1b6c4e03")]
public sealed record CreateOrder(string CustomerId, int Quantity) : ICommand<Guid>;
```

```csharp
public interface ITypeRegistry
{
    bool TryGetType(ClassId id, [NotNullWhen(true)] out Type? type);
    bool TryGetClassId(Type type, out ClassId id);
    IReadOnlyCollection<RegisteredType> All { get; }
}
```

`RegisteredType` carries the id, the type, a human-readable name, and a
metadata bag. The GUID is what goes on the wire and into the database — stable
across renames, 16 bytes, indexable. The name exists for telemetry, logs, and
error messages, which should never make a human read a GUID.

Two sharp edges worth stating up front:

- **Attribute arguments cannot be `Guid`.** The attribute takes a string,
  parsed exactly once when the registry is built. The analyzer validates the
  format at compile time so a malformed id is a build error, not a startup
  crash.
- **`Id<T>`'s EF converter assumes a generic argument.**
  `PropertyBuilderExtensions.ApplyIdConversion` reads
  `idType.GetGenericArguments()[0]`, so a non-generic `ClassId` implementing
  `IId` would break it. Either model `ClassId` as a closed `Id<T>` or give it
  its own converter and comparer; do not quietly widen the existing one.

### Analyzer and code fix

The analyzer is not a convenience — it is what makes the generator's input set
trustworthy. It enforces the `[RequiresClassId]` contract **in both
directions**:

| Rule | Condition | Code fix |
| --- | --- | --- |
| missing id | non-abstract class implements a `[RequiresClassId]` interface, carries no `[ClassId]` | add one with a fresh GUID |
| spurious id | type carries `[ClassId]`, implements no such interface | remove the attribute |
| duplicate id | two types share a GUID | none — the author picks which is wrong |
| malformed id | the string does not parse as a GUID | replace with a fresh GUID |
| ineligible type | `[ClassId]` on an abstract class, an interface, or an open generic | remove the attribute |

Abstract types and interfaces are excluded because only instantiable types are
whitelisted. Open generics are excluded because one attribute cannot identify
every closure; if a generic contract has to cross the wire, its closures need
ids of their own.

Because the ids are **persisted**, changing or deleting one is a data-migration
event, not a refactor. A shipped-baseline file — the way public-API analyzers
use `PublicAPI.Shipped.txt` — turns an altered or removed id into a build
error instead of silently orphaning stored rows. Worth having from the start,
since the whole point of the GUID is that it outlives the type name.

### Population — source generator

An incremental generator (new `ArchPillar.Extensions.Primitives.Analyzers`,
`netstandard2.0`, mirroring `Localization.Analyzers`, with its code fixes in a
matching `.CodeFixes` project) emits explicit registration calls for the set
the analyzer has already guaranteed.

This keeps `IsTrimmable` and `IsAotCompatible` true on Primitives and
Commands alike, which assembly scanning would force to false. It also makes the
whitelist a build artifact: what is registered is visible in generated source
and diffable in review.

Registrations are emitted **per module**, so a module's whitelist arrives with
the module rather than through a separate parallel mechanism. The generator
walks the host compilation and its referenced assembly symbols, so a host
referencing a shared contracts assembly picks those contracts up without
per-assembly ceremony; that work is compile-time only.

### Persistence

Both stores ship together. `Primitives.EntityFrameworkCore` gains the entity,
its configuration, and a reconciliation step:

- Class ids declared in code but absent from the table are **inserted**.
- Rows present in the table but absent from code are **kept, never deleted** —
  historical rows elsewhere may still reference them. They are reported as
  orphaned.
- A GUID bound to a different type than the table records, or a type whose
  GUID has changed, is a **hard error**. This reconciliation is the point of
  persisting: it turns a class of silent production data corruption into a
  startup failure.

Reconciliation runs from the module system's initialization hook, so each
module's whitelist is checked against the table before the host serves
traffic.

Deliverables: `ClassId`, the two attributes, `ITypeRegistry` and its in-memory
implementation, the analyzer with its five rules, the code-fix provider, the
shipped-id baseline, the generator, the EF entity plus reconciliation, tests
for all of it, and the docs (`docs/primitives/internals/SPEC.md` and the
user-facing pages).

### What it buys Commands

Resolution becomes three distinct outcomes instead of one generic 500 — the
distinction the review flagged as missing (A2) and the reason choreography
works at all:

| Situation | Meaning | Response |
| --- | --- | --- |
| class id not in the type registry | unknown contract | reject |
| in the type registry, no invoker | known, not owned here | routable — ask discovery |
| in both | owned here | dispatch locally |

It also closes the deserialisation-trust problem by construction: there is no
path from wire bytes to a CLR type that is not already whitelisted. The
receiving host never calls `Type.GetType`.

---

## Phase 2 — `Commands.EntityFrameworkCore`

New package, depending on `Microsoft.EntityFrameworkCore.Relational`, with
`IsAotCompatible=false` to match `Primitives.EntityFrameworkCore`.

### Transactions

```csharp
services.AddCommandTransactions<OrdersDbContext>();
```

Nesting is inferred from `DbContext.Database.CurrentTransaction`, not from
dispatcher state — this keeps the Commands core ignorant of EF and avoids
introducing ambient dispatch state for one consumer's benefit.

- **No ambient transaction** — begin one, run the dispatch, commit when
  `context.Result is { IsSuccess: true }`, roll back otherwise, and roll back
  on a throw that escapes the inner chain (the documented sample middleware
  does not).
- **Ambient transaction present** — join it. No new transaction, no savepoint
  by default. A nested failure dooms the outer transaction because the outer
  middleware sees the failed result.

Two edges to handle rather than discover in production:

- **Execution strategies.** `EnableRetryOnFailure` plus a manually started
  transaction throws in EF Core. Detect a retrying strategy and fail at
  startup naming it, with an explicit opt-in for wrapping the dispatch in
  `CreateExecutionStrategy().ExecuteAsync(...)` — that re-runs the whole
  handler, which is only safe if handlers are re-entrant.
- **`SaveChangesAsync` stays in the handler.** No unit-of-work middleware that
  saves on the handler's behalf. Checkpoint recovery depends on a nested
  handler having actually written; a deferred single save at the end would
  make checkpoints meaningless.

### Checkpoints

Opt-in and caller-side — no attributes, no per-command configuration:

```csharp
await using ICommandCheckpoint checkpoint = await _checkpoints.BeginAsync(cancellationToken);

OperationResult result = await _dispatcher.SendAsync(new ReserveStock(...), cancellationToken);
if (result.IsFailure)
{
    await checkpoint.RollbackAsync(cancellationToken);   // database *and* change tracker
    // the outer command carries on — this is the entire point
}
```

`BeginAsync` creates a savepoint and, when recovery is enabled, snapshots the
change tracker. Disposal without an explicit rollback releases the savepoint.

### Recovery

Opt-in, because the snapshot costs memory proportional to tracked entities:

```csharp
services.AddCommandTransactions<OrdersDbContext>(o => o.Recovery = CheckpointRecovery.ChangeTracker);
```

**EF Core does not revert the change tracker when a savepoint is rolled
back.** This is the trap the feature exists to close. After a nested
`SaveChangesAsync` the tracker marks entities `Unchanged` and accepts their
values; rolling the savepoint back removes the rows but leaves the tracker
believing they are persisted. The next outer save then writes a wrong picture,
or nothing at all.

At `BeginAsync`, snapshot every `EntityEntry`: the entity reference, `State`,
a clone of `CurrentValues`, a clone of `OriginalValues`, and whether the key is
temporary. At `RollbackAsync`, after the savepoint rollback:

| Entry situation | Action |
| --- | --- |
| tracked now, absent from the snapshot | detach — added after the checkpoint, its row is gone |
| tracked in both | restore original values, then current values, then `State` |
| in the snapshot, no longer tracked | **throw** — silently re-attaching would fabricate state |

Order matters: values before `State`, or EF re-derives modified flags from the
wrong baseline.

Limitations that belong in the SPEC, not a footnote:

- Scalar values are restored; **collection navigations and graph shape are
  not**. An entity removed from a parent's collection after the checkpoint
  stays removed in memory. Callers mutating graphs inside a checkpoint must
  reload.
- Database-generated values from a rolled-back save are discarded with the
  detached entries.
- Providers reporting `SupportsSavepoints == false` (InMemory, Cosmos)
  **throw at `BeginAsync`** rather than degrade to a no-op that looks like it
  worked.
- One `DbContext` per transaction scope. Multi-context and distributed
  transactions are out of scope.

Tests run against real PostgreSQL — the repo already has
`PostgresTestDatabase`, and savepoints are provider behaviour the InMemory
provider cannot verify.

---

## Phase 3 — `Commands.Remote`

### Wire contract

```
CommandEnvelope  { ClassId, Payload, CorrelationId, Deadline, TraceParent }
ResultEnvelope   { Status, Problem, Value }
```

`OperationResult` is already RFC 7807-shaped and annotated for
`System.Text.Json`, with `Exception` correctly `[JsonIgnore]`d, so the problem
body serialises as-is.

The invariant that makes the monolith-to-microservices migration real:

> A remote handler's `OperationResult` reaches the caller byte-identical to
> what an in-process handler would have returned.

Transport failures are the deliberate exception, and stay distinguishable:

| Situation | Result |
| --- | --- |
| remote handler returned a failure | that failure, verbatim |
| host unreachable | `ServiceUnavailable`, `transport_unavailable` |
| deadline elapsed | `GatewayTimeout`, `deadline_exceeded` |
| host does not own this class id | `NotImplemented`, `unknown_command` |

### Routing

Routing belongs in the router — that is its job, and a separate "is this
remote?" middleware would be a second door to the same decision. Resolution
becomes: local invoker, else a route from the route table, else the
unknown-contract failure. Telemetry, exception handling, and call sites are
untouched.

**The router and validation always run on the host that owns the handler.**
Remote dispatch means the owning host's own pipeline runs the command, so
validation executes next to the handler inside that host's transaction. Bolted
on as client-side pre-validation, the TOCTOU guarantee dies silently.

**Transactions do not cross the wire.** A transaction middleware registered
outside the router will wrap a remote dispatch too, opening a local
transaction for work happening elsewhere. A remote command that fails cannot
be rolled back by the caller's transaction; the caller compensates. Phase 2's
checkpoints cover local work only. This is the honest limit of the promise and
should be the first thing the remote SPEC says.

### Serialisation

Captured at registration in both directions, so nothing is reflected at
dispatch time and trimming stays intact:

```csharp
services.AddRemoteCommand<CreateOrder, Guid>(
    OrdersJsonContext.Default.CreateOrder,
    OrdersJsonContext.Default.Guid);
```

Class id to type resolution goes through the Phase 1 registry. Never
`Type.GetType`.

### Channel

`HTTP/2 + System.Text.Json` over `HttpClient` for v1, behind an
`ICommandChannel` seam so a socket transport can land later without touching a
handler. Smallest surface, no new dependency, works through every proxy.

Server side: one endpoint that dispatches the envelope through the local
pipeline and returns the result envelope. Client cancellation maps to request
abort; the deadline maps to a request timeout and to the handler's
`CancellationToken` on the far side.

Semantics are **at most once, no automatic retry**. A command is not
idempotent unless its author says so, and the library will not guess. Retry is
a caller decision, expressible as a middleware.

---

## Phase 4 — `Commands.Discovery`

Discovery answers one question: which hosts own this class id?

- **Advertisement.** A host exposes the class ids it has registered, built from
  the invoker registry intersected with the type registry — no reflection.
- **Resolution.** `ICommandRouteResolver` maps class id to candidate
  endpoints, refreshed on an interval, with the last good answer cached so a
  registry outage does not take callers down.
- **Selection.** Round-robin across healthy candidates with a circuit breaker
  that ejects an endpoint after repeated transport failures and probes it
  back.
- **Conflict.** Two hosts advertising one class id are assumed
  interchangeable. Because the id is a GUID minted per contract, a breaking
  change mints a new one; the old handler stays registered until every caller
  moves.

Backends: static configuration first — it works everywhere and is testable —
then adapters over whatever the platform provides, behind the same resolver
interface.

---

## Sequencing

```
Phase A  clear the ground ............... gates everything
           (module system) .............. gates 1, out of scope here
Phase 1  type registry (Primitives) ..... gates 3 and 4
Phase 2  EF transactions + checkpoints ... independent; highest immediate value
Phase 3  remote execution ............... needs 1
Phase 4  discovery ...................... needs 3
```

Phase 2 depends only on Phase A, so it can start before the module system
lands.
