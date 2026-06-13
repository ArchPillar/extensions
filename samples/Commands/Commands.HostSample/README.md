# Commands.HostSample

Demonstrates ArchPillar.Extensions.Commands inside a `Microsoft.Extensions.Hosting` console application.

## What it shows
- Two commands: result-bearing (`CreateOrder`) and no-result (`CancelOrder`).
- Handlers derived from `CommandHandlerBase<TCommand[, TResult]>` using the status factories (`Ok`/`Created`/`NotFound`) and assert helpers (`EnsureFound`).
- Per-handler validation via the `IValidationContext` fluent helpers.
- Implicit conversion of `OperationResult` to `Task<OperationResult>` on the sync `NoContent()` return path.
- `throw OperationResult` — unwrapped to an `OperationException`, caught by the exception middleware and turned back into an `OperationResult`.
- Telemetry: every dispatch produces an `Activity` on `CommandActivitySource.Name`.
- Optional batch handler: `CreateOrderBatchHandler` for atomic bulk inserts — one invalid item rejects the whole batch.

## Running
```bash
dotnet run --project samples/Commands/Commands.HostSample
```
Prints labelled sections to the console: the happy path, a validation failure, a not-found cancel, a successful batch, and a rejected batch — each annotated with the `[activity]` telemetry line for its dispatch.

## Notes
- Data is held in an in-memory store and reseeded each run, so output is stable.
