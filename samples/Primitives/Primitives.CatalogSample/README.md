# Primitives.CatalogSample

Demonstrates ArchPillar.Extensions.Primitives `OperationResult` and typed `Id<T>` in a small in-memory product catalog.

## What it shows
- Success factories: `Ok` / `Created` with `TValue` inferred from the argument.
- Failure factories returning `OperationFailure`, implicitly converted to `OperationResult<Product>`: `NotFound`, `Conflict`, `BadRequest` with field errors.
- The `OperationProblem` / `OperationError` shape (type / title / detail, field-keyed `Errors`, `Extensions`) rendered with Spectre.Console.
- `IsSuccess` / `IsFailure` branching; `Unwrap()` to take the value on success; `ThrowIfFailed()` to surface a failure as an `OperationException`.
- Implicit conversions: `TValue` → `OperationResult<TValue>`, and `throw OperationResult` → `OperationException`.

## Running
```bash
dotnet run --project samples/Primitives/Primitives.CatalogSample
```
Prints labelled console sections: the happy path (a created product, an unwrapped lookup), then the failure paths (a not-found, a validation failure with field-keyed errors, a duplicate-SKU conflict), and finally a caught `OperationException` showing the carried status.

## Notes
- Data is held in an in-memory store with deterministic seeded ids and is reseeded each run, so the output is stable.
