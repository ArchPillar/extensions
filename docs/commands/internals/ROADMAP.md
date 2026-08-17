# Commands — roadmap

The plan for `ArchPillar.Extensions.Commands`, in two parts: everything that
runs in one process first, then everything that crosses a process boundary.
Companion to [REVIEW.md](REVIEW.md), the assessment this plan answers.

Some phases are **platform work rather than Commands work** — the type
registry serves models, commands and events alike, and the module system is a
prerequisite designed elsewhere. They are planned here because the Commands
work is what forces them.

## The model

Everything below follows from three sentences:

> A **command** is an unbreakable unit of work with a potential side effect.
> Event handlers dispatch commands. Deferring work is queueing a command for
> later execution.

So the command boundary *is* the transaction boundary — the question "where
does the transaction go?" never arises. Every state change in the system goes
through one door, which is what makes validation, authorization, telemetry and
transactionality uniform rather than conventions enforced by review.

Two consequences worth stating before the feature list, because they are
easy to break by accident:

- **A command that cannot be rolled back is not unbreakable.** Charging a card
  or sending mail inline breaks the guarantee. Those get *deferred* — queued
  as a command that runs after the transaction commits — which is how the
  outbox falls out of the model rather than being bolted onto it.
- **Nesting absorbs the inner unit.** When A dispatches B sequentially, B joins
  A's transaction and is no longer independently unbreakable. The unbreakable
  unit is the *outermost* command. Checkpoints exist to give a nested step its
  boundary back.

## Two rules that apply everywhere

**Every limitation is enforced at startup or first dispatch, never left as
prose.** Whoever hits these will hit them once, under pressure, without having
read the spec.

**The library never decides on the consumer's behalf.** Where a trade-off is
theirs, it either refuses to guess or does exactly what was asked — never a
middle state where it silently escalates. And anything offered is built to
production quality even when it is not the recommended path.

---

# Part I — single host

Nothing here depends on any transport decision, and this is where most of the
value sits: boundary enforcement, one transaction across an execution, and
validation on the snapshot the handler writes against.

## Phase A — clear the ground

The review's blocking findings, plus the public surfaces that must not freeze
in the wrong shape.

1. **A per-dispatch metadata surface.** Principal, deadline and idempotency key
   have nowhere to live; `SendAsync(command, ct)` carries none of them and
   ambient state is banned. Public API change, so it lands first.
2. **Resolve ownership before the pipeline runs.** Local-versus-remote is
   currently discovered at the router — the innermost point — while
   resource-acquiring middleware sits outermost, so a transaction would pin a
   pooled connection around a dispatch that turns out to be remote. Ownership
   belongs on `CommandContext` before the first middleware.
3. **Fix the batch tier — do not delete it.** The review recommended removing
   the no-batch-handler fallback as a duplicate of a caller's loop. It is not:
   `SendBatchAsync` runs the pipeline **once** for N items, so one transaction
   wraps the group, where a `foreach` runs it N times and gets N transactions.
   `recommendations.md` agrees only for the case *"where you don't need batch
   atomicity"*, which is precisely what the fallback provides. What is actually
   wrong is the implementation: make `BatchResults` internal or give the
   context a typed slot, stop `ComposePerItem` flattening per-item statuses,
   and cover the typed path, which today has **zero tests** and the most
   runtime-cast risk.
4. **Missing-handler semantics**, and pick the **final status taxonomy now** —
   a locally missing handler is a deployment misconfiguration and needs its own
   `Problem.Type`, distinct from the cross-host outcomes in Part II. Choosing
   later breaks telemetry queries.
5. **Carry `Type? ResultType`** on the descriptor in place of `ProducesResult`.
6. **Open `ActivityKind` and `ParentContext`** on `CommandContext`, and tag the
   stable identity alongside `command.type` — `FullName` breaks dashboards on
   rename, which is why stable ids exist.
7. **Write down the nesting contract**: sequential nested dispatch shares the
   outer scope, concurrent dispatch does not.
8. **Close the test gaps**: re-entrant dispatch, cancellation propagation,
   registration traps.

### Primitives gaps found while planning

- No **502, 504 or 408** in `OperationStatus`; the cross-host taxonomy needs
  `GatewayTimeout`, and without it the canonical failures are numeric casts.
- `OperationStatus.None = 0` has no HTTP mapping and must be special-cased.
- `OperationProblem.Extensions` is `IReadOnlyDictionary<string, object?>`, so
  under source-generated JSON a value is an `int` in-process and a
  `JsonElement` across a wire — the identical-result promise breaks on the
  first validation failure. Needs a decided representation.
- `Authenticate` / `Authorize` / `Must` capture the predicate's **source text**
  via `[CallerArgumentExpression]`. Fine in local logs; it must not leave the
  process.

## Blocked on — the module system

Designed elsewhere. What the phases below need from it: a module owns its
commands one-to-one; modules communicate only through commands; a module owns
the remote channel that other subsystems piggyback on; a module has a single
aggregation point (it need not be an assembly) and a stable declared id; and
there is a post-build initialization hook.

## Phase 1 — the type registry (in Primitives)

A **universal** whitelist — every model, command and event — kept strictly
separate from `CommandInvokerRegistry`: the type registry knows what a command
*is*, the invoker registry knows who *handles* it.

This is single-host work despite reading like remoting infrastructure. Stable
type identity is what an outbox, an audit trail, saga state or a
permission system keyed on type all need, none of which involve a network.

**Identity is `(module id, class id)`.** Class-id uniqueness is only verifiable
within one compilation, so module scoping makes the guarantee achievable — and
it means nothing ever has to *derive* a command's owner. Module ids are
declared in code with a configured override for the vendor-collision case;
config-only ids would make identity depend on deployment and silently orphan
stored rows on an edit that looks free.

Only types **independently identified at a boundary** get ids — dispatched as a
root, persisted by reference, or deserialized standalone. A value object nested
in a payload does not; its shape belongs to its container. Framework types
needing ids belong to a reserved well-known module id.

**Analyzer**, enforcing `[RequiresClassId]` in both directions with code fixes:
missing id on a non-abstract implementer, spurious id on a non-implementer,
duplicate within a module, malformed GUID, and ineligible types (abstract,
interface, open generic). The guarantee is **per compilation** and the plan
says so: the module's aggregation point checks module-wide uniqueness at build
time, and the host's registry build is the last gate, hard-erroring on a
duplicate with both type names.

**Reverse lookup is not a function** once a module can register a type it does
not own. Forbid it — hard-error at startup when two loaded modules claim the
same CLR type.

**Generator**: an incremental generator emitting registrations for the
analyzer-guaranteed set, keeping trim and AOT compatibility where scanning
would forfeit them. Walking referenced assembly symbols has **no precedent in
this repo** — the Localization generator is syntax-only — so budget the marker
attribute and metadata cache rather than assuming them.

**Persistence** ships alongside the in-memory store. Reconciliation must be
**rename-invariant** (the CLR name is informational; renaming with the id
unchanged is a supported refactor), idempotent under concurrent replica
startup, and tolerant of a rolling deploy, or the registry table blocks an
application rollback.

**Contract evolution is deferred.** A shape baseline has no stable definition
while `[JsonConverter]` exists (`Id<T>` is the counterexample — a struct that
serialises as a bare string), transitive DTOs invalidate across projects, and
record positional parameters bind through constructors. What is needed first is
a normative JSON compatibility policy; the baseline caches that policy rather
than replacing it. It becomes mandatory only if independently-deployed peers
are ever supported (see Part II).

## Phase 2 — `Commands.EntityFrameworkCore`

New package on `Microsoft.EntityFrameworkCore.Relational`,
`IsAotCompatible=false` to match `Primitives.EntityFrameworkCore`.

**Modules do not have transaction boundaries.** One transaction spanning an
entire execution across many modules is the property that makes the modular
monolith worth having.

Nesting is inferred from `DbContext.Database.CurrentTransaction`, keeping the
core ignorant of EF. Sequential nested dispatch shares the outer scope and its
transaction. Concurrent nested dispatch does not — `DbContext` is not
thread-safe, so each concurrent invocation gets its own scope, context and
therefore **its own transaction**. Fanning out nested commands in parallel and
having them be one atomic unit are mutually exclusive, and that must be stated
rather than discovered.

Retrying execution strategies conflict with manual transactions and are
detected at startup. `SaveChangesAsync` stays in the handler.

**Checkpoints** are savepoints opened by the caller. Their justification is not
"undo partial work" — if a failed dependency just aborts everything, plain
rollback suffices. They earn their place when a coordinator wants to
**continue**: try another provider, skip an optional step, degrade. What they
preserve is the coordinator's *decision*, not just its data — a create that
validated "this does not exist yet" is still valid after rolling back, because
nothing outside the transaction moved.

**Recovery** is opt-in. EF does not revert the change tracker when a savepoint
rolls back, so after a nested save the tracker believes rows are persisted that
the database discarded. Snapshot each entry's state, original values, current
values and key temporariness; on rollback detach entries absent from the
snapshot, restore original-then-current-then-`State` for entries in both, and
throw for entries that vanished.

**Detaching does not sever inbound navigations**, and this is a corruption path
rather than a lossy one: a surviving `order.Payment` still referencing a
detached `Payment` gets re-inserted by graph traversal on the next save. Clear
reference navigations on surviving entries that point at detached ones, or
downgrade the claim from recovery to detection.

Limits: scalars restored, collection navigations and graph shape not; providers
without savepoint support throw rather than degrade; one `DbContext` per
transaction scope.

## Phase 3 — deferred commands

Queueing a command for later is the model's answer to work that cannot be part
of the current unbreakable unit — non-transactional side effects, retries, and
scheduled work. The row is written in the caller's transaction and drained
after commit, so an outbox is not a separate subsystem: it is the queue of
deferred commands, one shape and one drain loop whatever produced the entry.

This is where the stable identity from Phase 1 is consumed, and it is entirely
single-host.

---

# Part II — cross host

## When this applies

Three tests, and **all three must be yes**:

| Test | Question |
| --- | --- |
| deployment | Same build, deployed atomically, verified at connect? |
| data | Compute-only, owning no data of its own? |
| trust | Same security context — same cluster, same CA, same host? |

They select the same boundaries. When they disagree for a particular split,
that is the signal the topology is wrong rather than that a workaround is
needed.

What this **is**: a satellite — pulling extra computational resources into one
logical service. Same build means no version negotiation, no contract hashing
and no schema evolution; the protocol needs none of it, only a build check at
connect. Because the module code is present everywhere, a satellite that owns
no data can **degrade to local execution** when none are reachable: slower, not
broken.

What this is **not**:

- **A security boundary.** Commands live inside one security context. The
  channel verifies *workload* identity (mTLS, a Unix socket, network policy);
  *user* identity is delegated and believed, not validated. Delegation is
  transitive and unattenuated, so the whole call graph must be in the trust
  domain, not just the immediate pair.
- **A transport for permanently split services.** If two things deploy
  independently they are separate services and need a designed API seam, or —
  more usually — they should communicate through **events**, which is both the
  industry norm for cross-service writes and infrastructure that already
  exists here.
- **A microservices framework.** Supporting independently-deployed peers costs
  the whole contract-evolution apparatus (additive-only enforcement, unknown
  members tolerated, the JSON compatibility policy). Deferred, and kept
  *addable* by preserving exactly two properties from day one: tolerate unknown
  members on receive, and keep identity stable.

## Phase 4 — the module channel

The **module** owns the channel; commands are one subsystem piggybacking on
it, queries will be another.

**HTTP, not gRPC.** gRPC's advantages here — first-class deadlines, reliable
cancellation, a retryable status vocabulary — are *conventions* and portable to
any transport. HTTP's advantage is an *ecosystem* and is not. So borrow the
conventions and keep the transport. Its costs in this design are also real: a
second contract definition where the C# record is meant to be the single owner,
and typed services against one endpoint keyed by identity. Polyglot is not an
argument either way, since `.proto` and OpenAPI can both be generated *from* the
commands.

One dispatch is one ordinary HTTP request — no custom framing, so HTTP/2
multiplexing comes free and every intermediary keeps working. The same code
serves TCP, mTLS and **Unix domain sockets** (Kestrel listens on one,
`SocketsHttpHandler.ConnectCallback` dials one), which is the strongest
isolation story for a same-host satellite: no network exposure, filesystem
permissions as access control, no TLS to rotate.

`POST /{moduleId}/{kind}/{classId}`, so an ingress can route `/{moduleId}/*`
without knowing a class id, and per-command logs and rate limits work with
existing tooling. Note `POST` forecloses `GET` semantics — the query subsystem
needs its own mapping rather than inheriting this one.

`OperationStatus` maps to the real HTTP status. **The result body is the
discriminator**, with a distinct media type — *not* `application/problem+json`,
because ASP.NET Core emits that for routing 404s and unhandled 500s and every
`OperationProblem` property is nullable, so a framework 404 would deserialize
cleanly into a domain `NotFound`.

**Conventions borrowed**: a **relative** deadline converted to a monotonic
deadline per process and decremented per hop (gRPC chose relative precisely to
avoid clock-skew coupling); W3C `traceparent`/`tracestate`, with an
`ActivityKind.Client` span on the calling side or remote dispatches appear as
orphaned HTTP spans; `Idempotency-Key`; and **at most once, no automatic
retry**.

**Failure taxonomy — three classes:**

| Class | Examples | Caller may |
| --- | --- | --- |
| definitely not executed | connection refused, TLS failure, intermediary 503 | retry freely |
| executed and failed | a handler failure, verbatim | act on it |
| **unknown** | deadline elapsed, reset after send | neither retry nor compensate blindly |

The third is the dangerous one. A timeout is not a failure — the far side may
have committed with the response lost — so compensating it as though nothing
happened un-does real work. Resolve it by retrying with the same idempotency
key or by querying, never by blind compensation.

**Identity propagation**: the envelope carries **claims, not a token**, and the
channel hydrates the same scoped service that ASP.NET hydrates from a request,
so Commands never learns the difference. Carrying a token would invite the
receiver to validate it and grow a second authentication layer nobody designed.
Peer authentication is **required**, not defaulted off: fail at startup unless
mTLS is configured or the endpoint is loopback or a Unix socket.

**Instance routing**, for named instances of one module (per-tenant, per-region,
per-shard) — the same shape as tenant isolation. Instance is an *addressing*
concern, never part of identity. The routing key comes from the per-dispatch
options, else the execution context, else static configuration. Two hard rules:
an unresolvable key **fails**, never falls back to a default instance; and
**local-first resolution must be gated on the key matching**, or a host serves
tenant B's command from tenant A's database. A host that does not know its own
instance key with named instances configured fails at startup.

**Operational**: per-request L7 balancing is a stated deployment requirement,
since HTTP/2 multiplexing pins connections and DNS-only discovery has no
client-side balancer; `EnableMultipleHttp2Connections` defaults to false and
servers cap concurrent streams, so set it and emit a queue-depth metric;
`PooledConnectionLifetime` must be set or pooled connections survive DNS
changes; request body size limits; and report **which commands are remote in
this deployment** at startup, so chattiness becomes reviewable once, at the
right level, without touching call sites.

**Discovery** is not a phase. Module id binds to a service name in
configuration, DNS resolves it, a named `HttpClient` does the rest.

## Phase 5 — coordination (conditional)

**Open: this phase may not be needed.** If a satellite owns no data — which the
data test above requires — then no transaction ever crosses a boundary and
there is nothing to coordinate. It survives only if data-owning satellites are
in scope, and a data-owning satellite is arguably a service that has not
admitted it.

If it is built: `System.Transactions` distributed transactions are
**Windows-only** and therefore unavailable here. PostgreSQL's own two-phase
commit is available and **implicit** — `PREPARE TRANSACTION` is a try,
`COMMIT PREPARED` a confirm, `ROLLBACK PREPARED` a cancel, derived from an
ordinary transaction with no domain modelling. A prepared transaction is
disassociated from its session and stored on disk, so it does **not** pin a
connection across a request boundary.

That puts the cost in the right place: Try-Confirm/Cancel charges every domain
operation forever, paid by whoever writes it; this charges the framework once.
It needs a coordinator with durable decision state, startup recovery for
undecided transactions, a reaper bounding orphans, and
`max_prepared_transactions` as a documented deployment requirement.

**Opt-in and never inferred** — without it, a remote dispatch inside an open
transaction is a hard error; with it the behaviour is implicit and there is no
API to design against. A consumer who keeps it permanently is respected: no cap
that fails past a threshold, and a suppressible warning whose config flag
doubles as the recorded decision. The metric always reports.

TCC stays documented for holds long enough that blocking on locks is
unacceptable, since 2PC blocks and TCC does not.

---

## Sequencing

```
Part I   — single host
  Phase A  clear the ground ............ gates everything
           (module system) ............. gates 1, designed elsewhere
  Phase 1  type registry .............. needs the module system
  Phase 2  EF transactions ............ needs only A
  Phase 3  deferred commands .......... needs 1 and 2

Part II  — cross host
  Phase 4  the module channel ......... needs 1
  Phase 5  coordination ............... conditional; may not be needed
```

Phase 2 needs only Phase A, so it can start before the module system lands.

## Open questions

- Whether Phase 5 exists at all — see the data test.
- The normative JSON compatibility policy, if independently-deployed peers are
  ever supported.
- A wire-stable representation for `OperationProblem.Extensions`.
- The distinct media type for result bodies.
