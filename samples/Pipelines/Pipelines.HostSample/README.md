# Pipelines.HostSample

Demonstrates using `Pipeline<T>` inside a `Microsoft.Extensions.Hosting` application with full DI wiring.

## What it shows
- Middlewares and the handler are classes with constructor-injected dependencies (here `ILogger<T>`), registered via `AddPipeline<T>()` / `AddPipelineMiddleware<T, TMiddleware>()` and resolved from the container.
- Telemetry wiring via `ActivityMiddleware<T>`: `OrderContext` implements `IPipelineContext`, `AddPipelineTelemetry<T>()` wraps the pipeline, and an `ActivityListener` on `PipelineActivitySource.Name` prints each activity (with tags) to the console.
- Two short-circuit paths: a validation middleware that returns early on an invalid `OrderId`, and an authorization middleware that returns early for an anonymous user.

## Running
```bash
dotnet run --project samples/Pipelines/Pipelines.HostSample
```
Prints three labelled sections to the console: the happy path, a validation failure, and an authorization failure — each followed by the `[activity]` telemetry line and tags for that run.

## Notes
- No external setup; the context is a plain in-memory object created per run.
