# Pipelines.BuilderSample

Demonstrates building a `Pipeline<T>` by hand with the fluent builder — no DI container involved.

## What it shows
- The static `Pipeline.For<T>()` entry point and the fluent `PipelineBuilder<T>`.
- Combining lambda-based middlewares, a class-based middleware, and a class-based handler.
- A short-circuit path: an inline guard returns early on an invalid order, skipping later middlewares and the handler.

## Running
```bash
dotnet run --project samples/Pipelines/Pipelines.BuilderSample
```
Prints two labelled sections to the console: the happy path (all middlewares and the handler run, with before/after logging) and the short-circuit path (the guard skips the rest).

## Notes
- No external setup; the context is a plain in-memory object created per run.
