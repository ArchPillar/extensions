# Commands — design review

A point-in-time review of `ArchPillar.Extensions.Commands`, taken before the
transaction, remoting, and discovery work began. It judges the library both on
its own merits and as a foundation for those three features.

Findings are ordered most-important-first within each section. Line anchors are
against the tree at the time of review; treat them as pointers, not addresses.

Baseline at review time: `dotnet test tests/Commands.Tests` — 37 passed, 0
failed, zero warnings.

---

## A. The library as it stands

### A1. The batch tier is a second door that circumvents the type system

**Major (design)** — `CommandDispatcher.cs:110-141`, `Internal/CommandRouter.cs:103-142`, `CommandContext.cs:108-118`

The result-bearing batch path is where the design fights its own type system.
The router's no-batch-handler loop writes a generic `OperationResult.Ok()`
marker to `context.Result` and passes the real per-item results through a
second public, settable slot (`CommandContext.BatchResults`). The typed
dispatcher then re-discovers the payload by runtime pattern matching, and
`ComposePerItem` can fail with a synthesized 500 — "Batch per-item result at
index {i} was not `OperationResult<T>`" — for a condition that cannot be
expressed, and cannot be ruled out, in the type system. Three shapes land in
one `OperationResult?` slot and two are distinguishable only by casting.

Measured against the monorepo's own principles:

- **One door per concern.** `SendBatchAsync` without a batch handler does what
  a caller's `foreach { SendAsync }` already does — and `recommendations.md`
  says as much. The only real differentiator is single-pipeline-pass
  atomicity, which the batch-*handler* path already provides cleanly.
- **Subtraction is progress.** Deleting the iterate fallback removes
  `IterateBatchAsync`, `CommandContext.BatchResults`, the `Ok()` marker,
  `ComposePerItem`, `CommandInvokerDescriptor.ProducesResult`, the
  "unexpected result shape" arm, and the per-item validation inconsistency
  (A8). The typed dispatcher switch collapses to typed / failure / null-500,
  and the unexpected-shape state becomes unrepresentable.
- **Question the spec.** A batch is a command whose payload is a list.
  `BulkCreateOrders(IReadOnlyList<CreateOrder> Items) : ICommand<IReadOnlyList<Guid>>`
  with an ordinary handler gives one pipeline pass, one transaction,
  all-or-nothing validation, and a typed list result — for one record
  declaration, against roughly 350 lines of batch machinery.

This compounds under remoting: a wrapper command serialises like any other
command, while `SendBatchAsync` forces a batch envelope into the wire protocol
and the discovery catalogue.

**Recommendation.** Delete the no-batch-handler iterate fallback at minimum.
Seriously consider deleting the batch tier outright and documenting the
wrapper-command pattern. If any batch surface survives, make
`CommandContext.BatchResults` internal — it is router-to-dispatcher plumbing
exposed as public API.

### A2. Missing-handler handling is fragile, and the SPEC's startup-validation claim is false

**Major** — `Internal/CommandRouter.cs:24`, `Internal/CommandInvokerRegistry.cs:37-92`, `ServiceCollectionExtensions.cs:293-316`, `internals/SPEC.md:196`

Three related problems.

1. **The mechanism is accidental.** `registry.Get` throws
   `InvalidOperationException` from inside the router. The caller sees a 500
   only because `ExceptionMiddleware` happens to wrap the router. The SPEC's
   claim that a missing handler "produces `OperationResult.Failed(...)` at
   dispatch time" is satisfied by coincidence, not design. It also conflates a
   deployment misconfiguration with a runtime fault — telemetry records it as
   an ordinary 500 with nothing to distinguish it.
2. **Startup validation cannot do what the SPEC says.**
   `ValidateCommandRegistrations` iterates *registered* descriptors and
   resolves their handlers. A command type never registered at all is
   invisible to it. It catches broken constructor dependencies, never missing
   registrations. Fix the SPEC and `features.md:415-429`, or build something
   that delivers the promise.
3. **Batch-only registration is silently dropped.** `ResolveDescriptor`
   returns `null` when no single-command descriptor matches, before it ever
   looks at `_batches`. Register only `AddBatchCommandHandler<AddItem, H>()`
   and every dispatch fails with "No command handler registered for AddItem"
   while a batch handler *is* registered — and `ValidateCommandRegistrations`
   passes, because `BatchInvokerEntry` has no `ResolveHandler` at all.

### A3. `CommandInvokerDescriptor` is three jobs in one type

**Major (design)** — `Internal/CommandInvokerDescriptor.cs:6-39`, `Internal/CommandInvokerRegistry.cs:79-89`

The descriptor owns single-dispatch invocation, batch invocation (nullable
legs grafted on by the registry's recomposition), and startup-validation
support — plus a `ProducesResult` flag that exists only to serve the iterate
fallback. The nullable batch legs force the router to branch on delegate
presence and to assert the internal contract with
`ArgumentNullException.ThrowIfNull` — the wrong exception type for what is
state corruption, not an argument fault.

The mechanics themselves are sound: the linear scan is one-time and cached;
`GetOrAdd`'s possible duplicate factory execution is idempotent; last-wins
matches `IServiceCollection` semantics *and* is coherent, because the static
delegates for a given `TCommand` are identical across duplicate registrations
and `GetRequiredService` also returns the last one; the recomposition
allocates once per command type, not per dispatch. The smell is the job count,
not the implementation. Under A1 the descriptor collapses to one job
naturally.

### A4. Per-dispatch triple copy on the batch-handler path

**Minor** — `CommandDispatcher.cs:64-69, 94-99`, `ServiceCollectionExtensions.cs:186-207, 253-273`

A batch dispatch copies the caller's list into `IRequest[]`, then
`ValidateBatchAsync` copies it into `TCommand[]`, then `InvokeBatchAsync`
copies it again — three O(N) copies, two of which exist only because the
element type is erased to `IRequest` at the dispatcher and re-derived at the
leaf. A symptom of the erasure that remoting presses on harder (B2).

### A5. `IRequestHandler` is vestigial; `IRequest` is misnamed

**Minor (API surface)** — `IRequestHandler.cs:10`, `IRequest.cs:13`

`IRequestHandler` is implemented by all four handler interfaces and consumed
by nothing — no constraint, no collection, no dispatch logic. Pure
mediator-lineage plumbing; delete it before v1 locks the surface.

`IRequest` is different: a shared base is genuinely required, since the
untyped `CommandContext` needs a common type and `ICommand<T> : ICommand`
would wrongly make every typed command dispatchable through the no-result
overload. But the *name* contradicts the library's stated identity — the SPEC
insists this is not a mediator and has no queries, while the root of the type
tier advertises `IRequest`. Rename it, or record why the name stays.

### A6. Middleware registration order is a silent foot-gun

**Minor** — `ServiceCollectionExtensions.cs:36-39`

Built-in middlewares are appended when `AddCommands()` runs. A user who
registers `AddPipelineMiddleware<CommandContext, X>()` *before*
`AddCommands()` gets `X` outside both `CommandActivityMiddleware` and
`ExceptionMiddleware`: exceptions from `X` escape `SendAsync` as raw
exceptions rather than results, and its work is invisible to telemetry.
Nothing detects it. Document the failure mode at minimum; better, have
`AddCommands()` verify it is the first contributor and fail at registration.

### A7. Empty-batch shortcut bypasses the pipeline and is status-inconsistent

**Minor** — `CommandDispatcher.cs:59-62, 89-92`

An empty batch never touches the pipeline — no activity, no middleware, and no
batch handler, which can therefore never reject an empty list — diverging from
the SPEC's "every batch dispatch runs through `Pipeline<CommandContext>`
exactly once". The two overloads also disagree on what nothing-happened means:
`NoContent` (204) for the no-result form, `Ok` (200) for the typed form.

### A8. Batch iterate path leaves `context.Validation` dark and skips cancellation

**Minor** — `Internal/CommandRouter.cs:118-138`

Per-item validation runs in throwaway `ValidationContext` instances, so a
middleware inspecting `context.Validation` after `next()` sees an empty
accumulator even when validation aborted the batch — an interface whose
meaning depends on dispatch mode. The loop also never checks the cancellation
token between items, so a large batch of fast, token-ignoring handlers runs to
completion after cancellation.

### A9. Misleading synthesized-500 messages

**Nit** — `CommandDispatcher.cs:44-48, 115-119`

The `_` arm of the typed `SendAsync` switch reports "Command pipeline produced
no result" even when a result *was* produced with the wrong shape — for
instance a caching middleware short-circuiting with an untyped
`OperationResult.Ok()` for a typed command. Distinguish "no result" from
"wrong-shaped result" and name the expected type; these strings are what
someone debugs from. Separately, `ComposePerItem` flattens per-item success
statuses, so three `Created` (201) items compose to one `Ok` (200).

### A10. Test coverage gaps that matter

**Major (coverage)** — `tests/Commands.Tests/`

The existing tests are good as far as they go — the ordering test proves the
one invariant that justifies validation-in-router. But:

- **The typed batch path is entirely untested.** `BatchDispatcherTests` uses
  only the no-result `AddItem`. `SendBatchAsync<TCommand, TResult>`,
  `ComposePerItem`, the `Ok()`-marker handoff, and the typed batch-handler
  path have zero coverage — precisely the code with the most cast risk.
- **No re-entrant dispatch test**, which is the exact scenario the EF
  transaction work is built on.
- **No cancellation test** — the SPEC's "`OperationCanceledException`
  propagates unchanged" is unverified.
- **No test** for the batch-only-registration trap (A2.3), duplicate-registration
  last-wins, `ValidateCommandRegistrations` actually throwing on a broken
  handler, or the pipeline-produced-no-result 500.

---

## B. Readiness as a foundation

### B1. Re-entrant dispatch is safe, but the documented transaction pattern breaks on the first nested command

**Blocker for the EF work, as currently documented** — `CommandContext.cs:20-33`, `recommendations.md:177-199`

What happens today on a nested dispatch: `CommandContext` is constructed per
dispatch, so there is no context-reuse bug — this part is right. The nested
dispatch resolves the same scoped dispatcher, pipeline, and middleware
instances from the same DI scope and runs the chain again. The nested activity
parents correctly to the outer one via `Activity.Current`, and the inner
`ExceptionMiddleware` absorbs inner failures into the inner result. The
substrate re-enters cleanly.

But the recommended `TransactionMiddleware` calls `BeginTransactionAsync` on
the same scoped `DbContext` unconditionally. On a nested dispatch EF Core
throws ("the connection is already in a transaction"), the inner
`ExceptionMiddleware` turns it into a 500, and the outer handler sees a
mystery failure. **The flagship documented pattern is incompatible with the
flagship upcoming feature.**

Structural gaps for savepoints and recovery:

1. **No nesting signal.** `CommandContext` has no parent and no depth, and the
   dispatcher cannot establish one without ambient state. A savepoint
   middleware *can* infer nesting from
   `DbContext.Database.CurrentTransaction != null` — EF state rather than
   dispatcher state — and that is probably the right integration design, since
   it keeps the core ignorant of EF. Decide it explicitly and write it into
   the SPEC.
2. **No per-dispatch state slot.** `CommandContext` has no extension bag, so a
   checkpoint middleware has nowhere on the dispatch to stash a savepoint name
   or a change-tracker snapshot. Carrying that state in locals across the
   middleware's own `await next(...)` does suffice under strict nesting —
   worth stating as the contract rather than leaving implicit.
3. **Scope sharing is an unstated policy.** Nested commands share every scoped
   service with the outer command. For savepoint recovery that is exactly what
   is wanted — same `DbContext`, same change tracker — but it forecloses
   scope-per-command designs and the docs never say so.

The good news: validation-inside-the-router **survives nesting**. A nested
command's validation runs inside whatever savepoint the nested pass opened, so
the TOCTOU argument holds at every depth.

### B2. Result-type erasure blocks a remote dispatch path

**Blocker for remoting** — `CommandContext.cs:79, 108`, `Internal/CommandInvokerDescriptor.cs:28`

`CommandContext` carries a `Type` and an untyped `OperationResult?`; the
descriptor carries only a `ProducesResult` bool. Anything receiving a
serialised response has no way to know which CLR type to deserialise the
payload into, and a wrong guess degrades quietly into the A9 500. The generic
knowledge exists only inside `SendAsync<TResult>` and is discarded at the
context boundary.

Carry `Type? ResultType` on the descriptor (replacing `ProducesResult`), and
give the registration site somewhere to capture static, AOT-safe
serialisation delegates the same way it already captures the invoke delegate.

Validation-in-the-router **survives remoting** — but only if remote dispatch
means "the owning host's own pipeline runs the command", so validation
executes next to the handler inside the host's transaction. Bolted on as
client-side pre-validation, the TOCTOU guarantee dies silently. This invariant
belongs in the remoting design: *the router and validation always run on the
host that owns the handler.*

### B3. `Type` is the command's only identity

**Blocker for discovery, major for remoting** — `CommandContext.cs:31, 79, 133`, `Internal/CommandInvokerRegistry.cs:8`

Everything keys on runtime `System.Type`: the registry cache, the descriptor
match, the `command.type` telemetry tag. `Type.FullName` is not a stable wire
identity — it moves with namespace refactors, does not version, and couples
both processes to identical assembly layout. Nothing in the design reserves a
place for a stable identifier.

*Resolved after review:* a separate type-registry subsystem keyed on a
`[ClassId]` GUID, distinct from the invoker registry. See
[ROADMAP.md](ROADMAP.md).

### B4. `CommandContext` hardcodes telemetry parentage and kind

**Major for remoting** — `CommandContext.cs:124-127`

`ActivityKind => ActivityKind.Internal` and `ParentContext => default` are
fixed on a sealed class. The Pipelines SPEC explicitly designed
`IPipelineContext.ParentContext` for "a remote parent parsed from a
`traceparent` header" — Commands seals that door. A host dispatching a
received command needs `ActivityKind.Server` and an injected parent; the
client side wants `ActivityKind.Client` on the outbound span. Plan a way to
supply both before the wire format is fixed, because `traceparent`
propagation has to be in the protocol from v1.

### B5. Batch and the wire

**Minor (consequence of A1)** — `ICommandDispatcher.cs:67-109`

If `SendBatchAsync` survives, remoting and discovery must answer: does the
protocol carry a batch envelope? Does discovery advertise batch capability
separately from single capability, given they are separately registered? Does
a remote host without a batch handler fall back to iterating, with different
atomicity than the caller expects? All three questions disappear if a batch is
just a command carrying a list — the strongest practical argument for A1.

---

## C. Verified good

- **The pipeline substrate is excellent.** Pre-composed delegate chain,
  snapshot semantics, zero-allocation hot path, honest lifetime rules.
  Commands reuses it rather than inventing a parallel mechanism — one door, as
  promised.
- **The AOT and trim claims are real.** Registration-site static delegates
  capturing only generic parameters, `[DynamicallyAccessedMembers]` on handler
  type parameters, no `MakeGenericMethod`, no runtime reflection. The lazy
  registry's `GetOrAdd` with an idempotent factory is correct under
  concurrency.
- **Validation design is thoughtful.** Accumulate-then-fold with explicit
  status precedence, `[CallerArgumentExpression]` field capture, structured
  `Extensions`, and a regex timeout defaulting to one second against ReDoS — a
  detail most libraries miss. Validation-in-router is the standout decision:
  the TOCTOU justification is sound, the ordering test proves the invariant,
  and it survives both nesting and correctly-shaped remoting.
- **Exception and cancellation semantics match the spec.**
  `OperationException` unwrap, `OperationCanceledException` propagation, and
  the activity middleware's result-slot re-inspection after the inner chain
  returns, with a non-async fast path when no listener is attached.
- **The docs are unusually accurate.** Nearly every SPEC claim checked matches
  the code — the DI registration table and order, telemetry tags and naming,
  error rules, batch path selection, last-wins semantics. The divergences found
  are enumerated in A2.2, A2.3, A7, and B1.

---

## Top actions

1. Delete the no-batch-handler iterate fallback; seriously consider the whole
   batch tier in favour of wrapper commands — A1, B5.
2. Make the router write the missing-handler failure directly; correct the
   startup-validation claim; fail fast on batch-only registration — A2.
3. Before feature work: carry `ResultType` on the descriptor (B2), open
   `ActivityKind` and `ParentContext` (B4), and write down the nesting
   contract — savepoint middleware keyed on EF state, scope sharing made
   explicit, the documented `TransactionMiddleware` fixed (B1).
4. Close the test gaps: typed batch path, re-entrant dispatch, cancellation
   propagation, registration traps — A10.
