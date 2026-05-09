# Changelog — ArchPillar.Extensions.Commands

Notable behaviour changes between revisions of the in-development branch.
This is *not* a published-package release log — there is no published
release yet. It exists so reviewers tracking the branch can spot semantic
shifts that the type signatures alone don't reveal.

## Unreleased

### Behaviour changes

- **Batch dispatch now always runs through the pipeline as a single outer
  pass — including the no-batch-handler case.** Previously
  `SendBatchAsync` without a registered batch handler invoked
  `SendAsync` per item, giving each command its own pipeline pass and
  its own wrapping middleware execution (so a `TransactionMiddleware`,
  for instance, opened N transactions). It now constructs one batch
  `CommandContext` and runs the pipeline once; the router iterates the
  input list internally and calls the per-command
  `ICommandHandler<TCommand>` for each item (validation then handler,
  bailing on the first failure). One transaction wraps the whole
  batch, one activity records `command.batch.size`, and the exception
  middleware catches throws from across the batch.

  *Migration:* if you depend on per-item middleware execution
  (per-item transactions, per-item retry budgets, per-item activities),
  fan out yourself with `SendAsync` in a `foreach` instead of calling
  `SendBatchAsync` — the fall-through behaviour the dispatcher used
  to provide.

- **Batch shape is now all-or-nothing at the validation gate.**
  `IBatchCommandHandler<TCommand>` gains a default-no-op
  `ValidateAsync(IReadOnlyList<TCommand>, IValidationContext, …)`. The
  batch handler owns validation across the whole list; if any
  validation entry is recorded, `HandleBatchAsync` is not invoked.
  Per-command `ICommandHandler<TCommand>.ValidateAsync` is no longer
  consulted on the batch-handler path.

- **`SendBatchAsync` return shape collapsed.**
  `SendBatchAsync<TCommand>` now returns `Task<OperationResult>`;
  `SendBatchAsync<TCommand, TResult>` returns
  `Task<OperationResult<IReadOnlyList<TResult>>>`. Either the batch
  ran and produced its result, or it produced one failure that aborted
  it. The previous `IReadOnlyList<OperationResult>` shape with
  per-item failure markers (`batch_rejected` for would-have-been-valid
  items) is gone.

### Telemetry

- `CommandActivityMiddleware` now reads `context.Result` after the
  inner chain returns and marks the activity `Error` with the failure
  detail when it sees a failure result (plus a `command.status` tag).
  Previously the inner `ExceptionMiddleware` absorbed throws into
  `context.Result`, which meant the outer activity middleware always
  saw "success" and recorded successful activities for failed
  dispatches.
