# Primitives.BlazorSample

A self-contained Blazor WebAssembly client that produces, displays, and round-trips `OperationResult` / `OperationProblem` from `ArchPillar.Extensions.Primitives` entirely in the browser.

## What it shows
- A form component validates input and produces `OperationResult<ProductDraft>` — `Ok` on success, `BadRequest` with field-keyed errors on failure.
- Rendering the contrast in the UI: a success value versus the `OperationProblem` / `OperationError` shape that describes a failure.
- Deserializing a canned `application/problem+json` payload into an `OperationProblem` with `System.Text.Json` and rendering it — the result round-tripping from the wire.
- Trim-safe / AOT-friendly, BCL-only Primitives: no server dependency, the whole sample runs client-side from a single `dotnet run`.

## Running
```bash
dotnet run --project samples/Primitives/Primitives.BlazorSample
```
Open the printed `http://localhost:NNNN` and navigate between `/` (the validation form) and `/problem` (the canned problem+json viewer).

- On `/`, submit a valid name and a positive price to see the `Ok(draft)` success card; submit a blank name and a non-positive price to see the `BadRequest` problem with two field errors.
- On `/problem`, the canned payload deserializes into an `OperationProblem` and renders identically through the shared display.

## Notes
- No backend — the client validates and renders everything itself; there is no server to start.
- Builds without the `wasm-tools` workload and with no AOT or trimming; it is a plain Blazor WASM build.
- Fully client-side: components live under `Pages/` and `Shared/`, validation under `Validation/`.
