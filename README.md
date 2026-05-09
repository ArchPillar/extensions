# ArchPillar Extensions

A collection of standalone .NET libraries that grew out of personal needs during real-world projects. Published in case they help anyone else. :)

## Libraries

### [ArchPillar.Extensions.Mapper](docs/mapper/)

Explicit object-to-object DTO mapping and LINQ/EF Core expression projection. One definition drives both in-memory mapping and IQueryable projection — with full IDE traceability.

### [ArchPillar.Extensions.Pipelines](docs/pipelines/)

A lightweight, allocation-free async middleware pipeline. `Pipeline<T>` composes `IPipelineMiddleware<T>` steps around an `IPipelineHandler<T>` terminal step as pre-built nested lambdas — reusable across invocations with zero per-call allocations on the synchronous hot path. Ships with built-in `Microsoft.Extensions.DependencyInjection` integration and a drop-in `System.Diagnostics.Activity` middleware for distributed tracing.

### [ArchPillar.Extensions.Primitives](docs/primitives/)

Foundational types for the rest of the family. Ships `OperationResult` / `OperationResult<TValue>`, `OperationProblem` (RFC 7807 `application/problem+json`-shaped), `OperationError`, `OperationStatus` (HTTP-aligned enum), `OperationFailure` and `OperationException`. AOT/trim-safe, zero dependencies beyond the BCL. Types live under the `ArchPillar.Extensions.Operations` namespace.

### [ArchPillar.Extensions.Commands](docs/commands/)

A small, in-process command dispatcher built on Pipelines and Primitives. `ICommand` and `ICommand<TResult>` flow through a single shared `Pipeline<CommandContext>`; cross-cutting concerns (transactions, logging, authorization) plug in as middlewares. Validation lives on the handler (`[CallerArgumentExpression]` auto-captures field names) and runs inside the pipeline so any user-supplied transactional middleware wraps both validation and execution. AOT/trim-safe, no runtime reflection at dispatch.

## Repository Structure

```text
├── src/
│   ├── Mapper/                            # Core mapping library
│   ├── Mapper.EntityFrameworkCore/        # EF Core integration
│   ├── Pipelines/                         # Pipelines library (includes DI extensions)
│   ├── Primitives/                        # OperationResult / OperationProblem family
│   └── Commands/                          # In-process command dispatcher
├── tests/
│   ├── Mapper.Tests/                      # Unit and integration tests
│   ├── Mapper.OData.Tests/                # OData-specific tests
│   ├── Pipelines.Tests/                   # Pipelines unit + allocation + DI tests
│   ├── Primitives.Tests/                  # OperationResult / OperationProblem tests
│   └── Commands.Tests/                    # Dispatcher, validation, batch, telemetry tests
├── benchmarks/
│   ├── Mapper.Benchmarks/                 # Mapper BenchmarkDotNet suite
│   ├── Pipelines.Benchmarks/              # Pipelines BenchmarkDotNet suite
│   └── Commands.Benchmarks/               # Commands BenchmarkDotNet suite
├── samples/
│   ├── Mapper/
│   │   ├── WebShop/                       # ASP.NET Core Web API sample
│   │   └── WebShop.OData/                 # OData endpoint sample
│   ├── Pipelines/
│   │   ├── Pipeline.BuilderSample/        # Direct (no-DI) Pipeline<T> sample
│   │   └── Pipeline.HostSample/           # Host-builder + AddPipeline<T>() sample
│   └── Commands/
│       ├── Command.HostSample/            # Host-builder dispatcher sample
│       └── Command.WebApiSample/          # ASP.NET Core Minimal-API sample
├── docs/
│   ├── mapper/                            # Mapper documentation and spec
│   ├── pipelines/                         # Pipelines documentation and spec
│   ├── primitives/                        # Primitives documentation
│   └── commands/                          # Commands documentation and spec
├── Directory.Build.props                  # Shared project properties
├── Directory.Build.targets                # Roslyn analyzer configuration
├── Directory.Packages.props               # Central package management
└── ArchPillar.Extensions.slnx             # Solution file
```

> **Note on samples:** The sample projects exist solely to demonstrate these libraries in action. They are not intended as guidance on how to structure services or products.

## Requirements

- .NET 8, 9, or 10
- C# 14

## License

[MIT](LICENSE)
