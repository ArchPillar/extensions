# Commands — roadmap

The plan for taking `ArchPillar.Extensions.Commands` from an in-process
dispatcher to one that also supports remote execution. Companion to
[REVIEW.md](REVIEW.md), the point-in-time assessment this plan answers.

Some phases here are **platform work rather than Commands work** — the type
registry serves models, commands and events alike, and the module system is a
prerequisite designed elsewhere. They are planned here because the Commands
work is what forces them; each gets its own spec when it is built.

## The through-line

The steady state is a **modular monolith plus choreography**: many modules in
one deployable, talking to a few peer services that are known and addressed
directly. Extracting a single module into its own service is a supported
move, not a trajectory — and it is usually driven by operations rather than
architecture (independent scaling, a different release cadence, a different
runtime, isolation for compliance, or a noisy neighbour).

Because the motivation is operational, **reversibility matters as much as
extraction**. A module pulled out because it was eating memory should be as
cheap to fold back when the workload changes. Designs that only go one
direction fail this.

The promise is that the handler code, the call site and the returned
`OperationResult` are identical in all cases — only registration changes.

The proof obligation is a sample with two hosts and one shared contracts
assembly, running both configurations with no difference outside `Program.cs`,
including a handler that authorizes against persisted state (see Phase A).

## Two rules that apply everywhere

**Every limitation of the remote path is enforced at startup or first
dispatch, never left as prose.** Whoever extracts a module will do it once,
under pressure, with no familiarity with this machinery, and will not have
read the limitations section. A handler that authorizes with no principal
hydration configured must fail at startup. A remote dispatch inside an open
transaction must fail loudly unless the fallback is explicitly enabled. Prose
is not a safety mechanism.

**The library never decides on the consumer's behalf.** Where a trade-off is
theirs to make, it either refuses to guess or does exactly what was asked —
never a middle state where it silently escalates. And where a capability is
offered, it is built to production quality even when it is not the
recommended path; friction-as-deterrent is dishonest.

---

## Phase A — clear the ground

The review's blocking findings gate the feature work, and several public
surfaces must change before they freeze.

1. **A per-dispatch metadata surface.** Three separate needs — principal,
   deadline, idempotency key — have nowhere to live. `SendAsync(command, ct)`
   carries none of them and ambient state is banned. This is a public API
   change and must land before the surface locks.
2. **Delete the no-batch-handler iterate fallback** (REVIEW A1). Removes
   `CommandContext.BatchResults`, the `Ok()` marker, `ComposePerItem`,
   `ProducesResult`, and the unexpected-shape 500. Decide separately whether
   the whole batch tier goes in favour of wrapper commands.
3. **Fix missing-handler semantics** (A2), and pick the **final status
   taxonomy now**: a locally missing handler is a deployment misconfiguration
   and needs its own `Problem.Type`, distinct from the unknown-contract and
   not-owned-here outcomes Phase 3 introduces. Choosing later breaks telemetry
   queries at the point they matter.
4. **Carry `Type? ResultType` on the descriptor** in place of
   `ProducesResult` (B2).
5. **Open `ActivityKind` and `ParentContext` on `CommandContext`** (B4), and
   tag the class id alongside `command.type` — `FullName` breaks dashboards on
   rename, which is the whole reason stable ids exist.
6. **Resolve ownership before running the pipeline.** Local-versus-remote is
   currently discovered at the router, which is the innermost point, while
   resource-acquiring middleware sits outermost. A transaction middleware
   would open a transaction — pinning a pooled connection — around a dispatch
   that turns out to be remote, exhausting the pool with transactions guarding
   no local work. Ownership belongs on `CommandContext` before the first
   middleware runs.
7. **Write down the nesting contract** (B1): a sequential nested dispatch
   shares the outer scope; a concurrent one does not (see Phase 2).
8. **Close the test gaps** (A10): re-entrant dispatch, cancellation
   propagation, registration traps.

### Primitives gaps found while planning

- `OperationStatus` has **no 502, 504 or 408**. The transport taxonomy needs
  `GatewayTimeout`; without it the canonical transport failures are unnamed
  numeric casts.
- `OperationStatus.None = 0` has no HTTP mapping and the mapping must
  special-case it.
- `OperationProblem.Extensions` is `IReadOnlyDictionary<string, object?>`.
  Under source-generated JSON, `Extensions["min"]` is an `int` locally and a
  `JsonElement` remotely — so the "identical result in both shapes" promise
  breaks on the first validation failure. Needs a decided representation
  before anything crosses a wire.
- `Authenticate` / `Authorize` / `Must` capture the predicate's **source text**
  via `[CallerArgumentExpression]` into the error body. Acceptable in local
  logs; over the wire it ships internal predicate code to callers. Needs to be
  stripped from remote responses.

---

## Blocked on — the module system

Designing `IModule` is out of scope here and tracked separately. What the
phases below depend on:

- **A module owns its commands**, one-to-one, and modules communicate only
  through commands.
- **A module owns the remote channel.** Transport, service-name binding, DNS,
  HTTP mapping, deadlines, trace and identity propagation live at the module
  layer, and Commands is one subsystem piggybacking on it. Queries will be
  another.
- **A module is an aggregation point** — the place that gathers everything it
  contains. It need not be an assembly, but it must have one such point,
  because that is where cross-file uniqueness is verified.
- **A module has a stable declared id**, with a configured override available
  (see Phase 1).
- **A post-build initialization hook**, to run the registry's database
  reconciliation before the host serves traffic.

---

## Phase 1 — the type registry (in Primitives)

A **universal** whitelisting subsystem: every model, command and event.
Nothing about it is command-specific, which is why it belongs in Primitives.
It stays strictly separate from `CommandInvokerRegistry` — the type registry
knows what a command *is*, the invoker registry knows who *handles* it.

### Composite identity

Identity is **`(module id, class id)`**, not a class id alone. Uniqueness of
class ids is only verifiable within one compilation, so scoping them per
module makes the guarantee achievable: a command copy-pasted between two
modules, GUID included, is no longer a collision.

The composite also removes the need to *derive* a command's owner. A client
holding only a contracts assembly knows the module from the identity itself,
so routing — `(module id, class id)` → service name → DNS — needs no separate
map and no reverse inference.

**Module ids are declared in code, with a configured override.** Config-only
ids would make identity depend on deployment, so a config change would
silently orphan every stored row referencing it. The override exists for the
real collision case — consuming two vendors' modules that ship the same id —
and changing one post-deployment is visibly a data migration rather than an
edit that looks free.

Which types get identity: those **independently identified at a boundary** —
dispatched as a root, persisted by reference, or deserialized standalone.
Commands, events and entities qualify. A value object nested inside a payload
does not; its shape is part of its container's contract and it carries no
identity on the wire.

Framework types that need ids (in Primitives and any shared kernel) belong to
a reserved well-known module id.

### Analyzer and code fix

`[RequiresClassId]` on an interface opts its implementations in. The analyzer
enforces the contract in both directions and is what makes the generator's
input set trustworthy:

| Rule | Condition | Code fix |
| --- | --- | --- |
| missing id | non-abstract class implements a `[RequiresClassId]` interface, carries no `[ClassId]` | add one with a fresh GUID |
| spurious id | type carries `[ClassId]`, implements no such interface | remove the attribute |
| duplicate id | two types in one module share a class id | none — the author picks |
| malformed id | the string does not parse as a GUID | replace with a fresh GUID |
| ineligible type | `[ClassId]` on an abstract class, an interface, or an open generic | remove the attribute |

The guarantee is **per compilation**, and the plan must say so. Cross-project
duplicates are invisible to it; the module's aggregation point is where
module-wide uniqueness is checked at build time, and the host's registry build
is the last gate — it hard-errors on a duplicate with both type names in the
message.

**Reverse lookup is not a function** once a type can be registered by a module
that does not own its source (the escape hatch for third-party types). Two
modules could claim one CLR type and mint different identities. Forbid it:
hard-error at host startup when two loaded modules claim the same type.

### Population — source generator

An incremental generator (new `ArchPillar.Extensions.Primitives.Analyzers`,
`netstandard2.0`, mirroring `Localization.Analyzers`, with code fixes in a
matching `.CodeFixes` project) emits explicit registrations for the
analyzer-guaranteed set, keeping `IsTrimmable` and `IsAotCompatible` true
where assembly scanning would force them false.

Note that walking referenced assembly symbols has **no precedent in this
repo** — `Localization.Analyzers`' generator is syntax-only over the current
compilation. Metadata traversal re-runs on every keystroke and is the
best-known incremental-generator performance trap, so it needs an
assembly-level marker attribute to prune the walk and a cache keyed on the
metadata reference. Budget that work rather than assuming it.

### Persistence

Both stores ship together. The persisted store is not speculative: this is a
library, and its consumers — outboxes, audit trails, saga state, permission
systems keyed on type — are downstream by definition.

Reconciliation, run from the module initialization hook:

- Composite ids declared in code but absent from the table are inserted.
- Rows present but absent from code are kept, never deleted, and reported.
- The comparison must be **rename-invariant**. The type's CLR name is
  informational and auto-updated; renaming a type with its id unchanged is an
  explicitly supported refactor and must not error. The invariant enforced is
  identity uniqueness, not name stability.
- The insert must be **idempotent and concurrency-safe** — N replicas
  reconcile simultaneously on deploy.
- A rolling deploy runs two versions against one table. Reconciliation must
  not hard-error a still-running old version, or the registry table blocks an
  application rollback.

### Contract evolution

A baseline file recording each type's contract shape was considered and is
**downscoped**, because the hash has no stable definition: `[JsonConverter]`
makes wire shape statically unknowable (`Id<T>` is exactly this case — a
struct that serialises as a bare string), transitive DTOs invalidate across
projects, record positional parameters bind through constructors, and
`IReadOnlyList<T>` versus `T[]` are wire-identical but CLR-distinct.

What is needed first is a one-page **normative JSON compatibility spec** —
are unknown members ignored, is adding an optional property breaking, how are
enums serialised. The fingerprint is a cache of that policy, not a substitute
for it. Until the policy exists, ship identity without shape tracking.

---

## Phase 2 — `Commands.EntityFrameworkCore`

New package on `Microsoft.EntityFrameworkCore.Relational`, with
`IsAotCompatible=false` to match `Primitives.EntityFrameworkCore`.

### Transactions

Modules do **not** have transaction boundaries. One transaction spanning an
entire execution across many modules is the property that makes the modular
monolith worth having, and nothing here should erode it.

Nesting is inferred from `DbContext.Database.CurrentTransaction` rather than
dispatcher state, keeping the Commands core ignorant of EF.

- No ambient transaction — begin one, commit on success, roll back on failure
  or on a throw escaping the inner chain.
- Ambient transaction present — join it.

**Sequential nested dispatch shares the outer scope** and therefore the
transaction. **Concurrent nested dispatch does not**: `DbContext` is not
thread-safe, so each concurrent invocation gets its own scope, its own
context, and therefore *its own transaction*. The consequence must be stated
plainly — fanning out nested commands in parallel and having them be one
atomic unit are mutually exclusive.

Retrying execution strategies conflict with manually started transactions and
are detected at startup. `SaveChangesAsync` stays in the handler.

### Checkpoints

Savepoints, opened explicitly by the caller.

The justification is not "undo partial work" — if a failed dependency just
aborts everything, a plain rollback suffices and checkpoints are dead weight.
They earn their place when the coordinator wants to **continue** after a
failed dependency: try another provider, skip an optional step, degrade.

What they preserve is the coordinator's *decision*, not just its data. A
create that validated "this does not exist yet" is still valid after a
rollback to the checkpoint, because nothing outside the transaction moved.
Without that, re-running validation can return a different answer and the
coordination is no longer sound.

### Recovery

Opt-in; the snapshot costs memory proportional to tracked entities.

EF does **not** revert the change tracker when a savepoint is rolled back.
After a nested `SaveChangesAsync` the tracker believes rows are persisted that
the database has discarded. At checkpoint, snapshot every entry's state,
original values, current values and key temporariness. After rollback:

| Entry situation | Action |
| --- | --- |
| tracked now, absent from snapshot | detach |
| tracked in both | restore original values, then current, then `State` |
| in snapshot, no longer tracked | throw — do not fabricate state |

Order matters: values before `State`, or EF re-derives modified flags from the
wrong baseline.

**Detaching does not sever inbound navigations**, and this is a corruption
path rather than a lossy one. A surviving `order.Payment` still referencing a
detached `Payment` will be rediscovered by graph traversal and re-inserted on
the next save. Reference navigations on surviving entries that point at
detached entries must be cleared, or the feature must downgrade its claim from
recovery to detection and throw.

Stated limits: scalars restored, collection navigations and graph shape not;
providers reporting `SupportsSavepoints == false` throw at checkpoint rather
than degrade; one `DbContext` per transaction scope.

---

## Phase 3 — remote execution over HTTP

### Why HTTP and not gRPC

gRPC's real advantages here — first-class deadlines, reliable cancellation, a
retryable/non-retryable status vocabulary — are **conventions**, and portable
to any transport. HTTP's advantage is an **ecosystem**, and it is not
portable. So borrow the conventions and keep the transport.

Its costs in this specific design: a second contract definition when the C#
record with its id is meant to be the single owner; a granularity mismatch
between typed services and one endpoint keyed by identity; and HTTP/2
end-to-end with trailers through every intermediary. The polyglot argument
does not apply either, since `.proto` and OpenAPI can both be *generated from*
the C# commands if they are ever needed. Protobuf-as-IDL is available without
protobuf-as-wire-format.

### Shape

One dispatch is one ordinary HTTP request — no custom framing. HTTP/2
multiplexing comes free, and every intermediary keeps working.

`POST /{moduleId}/{kind}/{classId}`, where `kind` demuxes subsystems.
Identity in the path means an ingress can route `/{moduleId}/*` without
knowing a single class id, and per-command access logs, rate limits and
dashboards work with tooling already in place.

Note that `POST` forecloses `GET` semantics, so the query subsystem must get
its own mapping rather than inheriting this one — do not let the command
mapping harden into *the* mapping before queries are designed.

`OperationStatus` maps to the real HTTP status and `OperationProblem` to a
problem body.

**The result body is the discriminator** — no marker header. But it must not
use `application/problem+json`: ASP.NET Core emits that for routing 404s,
405s, 415s and unhandled 500s, and every `OperationProblem` property is
nullable, so a framework 404 deserializes cleanly into a domain `NotFound`.
During a rolling deploy where a replica has not yet mapped the route, a caller
would read "it does not exist" as fact. Use a distinct media type.

### Conventions borrowed

- **Relative deadline on the wire**, converted to a monotonic deadline inside
  each process and decremented per hop. gRPC chose relative specifically to
  avoid clock-skew coupling, and an absolute deadline would make an
  unsynchronised container fail every inbound command with nothing in any log
  pointing at clocks.
- **W3C `traceparent`/`tracestate`**, which also feeds the `ParentContext`
  opened in Phase A. The client side needs its own `ActivityKind.Client` span
  carrying the identity, or remote dispatches appear as orphaned HTTP spans.
- **`Idempotency-Key`**, with the caveats below.
- **At most once, no automatic retry.** Retry is a caller decision.

### Failure taxonomy — three classes, not two

| Class | Examples | Caller may |
| --- | --- | --- |
| definitely not executed | connection refused, TLS failure, intermediary 503 before dispatch | retry freely |
| executed and failed | a handler failure, verbatim | act on the result |
| **unknown** | deadline elapsed, reset after the request was sent | neither retry nor compensate blindly |

The third class is the dangerous one and was initially missed. A timeout is
not a failure: the owner may have committed with the response lost in flight.
Compensating it as though it did not happen un-does real work. *Unknown* is
resolved only by retrying with the same idempotency key, or by querying —
never by blind compensation, and the status mapping must mark it
non-compensatable.

### Identity propagation

The envelope carries the caller's identity, and the module channel hydrates
the **same scoped service** from it that ASP.NET hydrates from a request. So
Commands never learns the difference and a handler calling
`validation.Authorize(...)` works identically in both shapes. Without this,
the "identical handler" promise is falsified by the first handler that
authorizes against persisted state — which is most of them.

Trust between services is out of scope for this library, but the channel must
require *some* configured authentication rather than defaulting to open. A
host with a handler that authorizes and no principal hydration configured
fails at startup.

### Idempotency

If it ships, all of this must be specified, or it is worse than not having it:
who mints the key (a per-call key means replay never fires); that failure rows
cannot exist because the write rolls back with the handler; comparing a
payload hash so the same key with a different payload cannot return another
operation's result; scoping keys to a caller so a guessed key cannot read
someone else's stored value; the behaviour when a retry races the original;
and a retention policy.

### Operational caveats

- **HTTP/2 multiplexing defeats L4 load balancing.** Few connections means a
  connection-level balancer pins traffic to one backend. Per-request L7
  balancing is therefore a **stated deployment requirement**, since the
  DNS-only discovery below has no client-side balancer to compensate.
- **`SocketsHttpHandler.EnableMultipleHttp2Connections` defaults to false**,
  and servers commonly cap around 100 concurrent streams per connection. The
  next request queues inside the handler with no attributable timeout —
  latency collapses under load while every metric stays green. Set it and emit
  a queue-depth metric.
- **`PooledConnectionLifetime` must be set**, or pooled connections survive
  DNS changes and a scaled or moved service keeps receiving traffic at stale
  addresses.
- **Request body size limits** on the command endpoint.
- The three routing outcomes are a **class-id enumeration oracle** if exposed
  to unauthenticated peers. Collapse unknown and not-owned-here externally.
- The client must be told **which CLR result type to materialize** — locally a
  pattern match, remotely a protocol decision. Write the rule down.

### Discovery

There is no discovery phase. Module id binds to a service name in
configuration, the service name resolves through **DNS**, and a named
`HttpClient` does the rest. A live registry is only warranted if ownership
moves at runtime, which it does not — ownership is a deploy-time fact.

---

## Phase 4 — coordination across a remote boundary

### The problem

A coordinator dispatching to several modules inside one transaction is
ordinary and correct in the monolith. Once a participant is remote, the
coordinator's validation can go stale mid-flight: it checked that something
did not exist, a later step failed, and the world it validated against has
already moved. This is a TOCTOU one level up from the per-command case the
router already solves, and only a shared transaction closes it.

The consequence for extraction is that the unit is not a module but a
**transactionally closed set** — a coordinator and everything it needs
atomically move together, or not at all. That is determinable rather than a
judgement call: follow the dispatch graph inside a transaction.

### Two-phase commit — opt-in, and a firefighting tool

`System.Transactions` distributed transactions are **Windows-only** (added in
.NET 7; any attempt on other platforms fails), so they are unavailable here
and not under consideration.

PostgreSQL's own two-phase commit is available and, importantly, **implicit**:
`PREPARE TRANSACTION` is a try, `COMMIT PREPARED` a confirm,
`ROLLBACK PREPARED` a cancel — derived from an ordinary transaction with no
domain modelling at all. A prepared transaction is disassociated from its
session and stored on disk, so it does **not** pin a connection across a
request boundary; a different connection resolves it later.

That places the cost correctly. The alternative below charges every domain
operation forever, paid by whoever writes it — including someone extracting a
module under pressure who has never heard of the pattern. This charges the
framework once, paid here, in advance.

What must be built for it: a coordinator with durable decision state,
recovery on startup for undecided transactions, and a reaper that rolls back
orphans (bounding the vacuum impact to seconds). `max_prepared_transactions`
defaults to 0 and becomes a documented deployment requirement. Drive it with
explicit SQL, not `System.Transactions`.

**It is opt-in and never inferred.** Without the opt-in, a remote dispatch
inside an open transaction is a hard error. With it, the behaviour is implicit
and the handler never knows — which is also what makes it impossible to design
against, since there is no API to build on.

If a consumer keeps it permanently, that is their decision and it is
respected. So: no cap that fails past a threshold, and the warning is
suppressible by configuration — that flag doubling as the recorded decision,
visible to whoever inherits the system. The metric always reports, because
information is not coercion. And the implementation is production quality,
with steady-state operations documented, not only the migration runbook.

### Try-Confirm/Cancel — for long holds

The same shape, modelled in the domain: a try that reserves with an expiry, a
confirm that cannot fail for business reasons, and an idempotent cancel. No
locks are held, because the reservation is ordinary domain state.

The split is that **2PC blocks and TCC does not**. A prepared transaction
holds its locks until resolved, which is fine for coordinations measured in
milliseconds and absurd for a seat held for twenty minutes. So 2PC is the
implicit default that makes extraction survivable, and TCC is the answer when
a hold is long enough that locking for its duration is unacceptable — at which
point the domain modelling is warranted and there is time to do it.

---

## Sequencing

```
Phase A  clear the ground ............... gates everything
         (module system) ................ gates 1, designed elsewhere
Phase 1  type registry (Primitives) ..... gates 3
Phase 2  EF transactions + checkpoints ... needs only A
Phase 3  remote execution over HTTP ..... needs 1
Phase 4  cross-boundary coordination .... needs 2 and 3
```

Phase 2 depends only on Phase A, so it can start before the module system
lands.

## Open questions

- The normative JSON compatibility policy, which contract-evolution tooling
  depends on.
- The representation for `OperationProblem.Extensions` that survives the wire
  identically to its in-process form.
- Whether the batch tier is deleted outright in favour of wrapper commands.
- The distinct media type for result bodies.
