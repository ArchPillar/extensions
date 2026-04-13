# ArchPillar Extensions

A collection of standalone .NET libraries that grew out of personal needs during real-world projects. Published in case they help anyone else. :)

## Libraries

### [ArchPillar.Extensions.Mapper](docs/mapper/)

Explicit object-to-object DTO mapping and LINQ/EF Core expression projection. One definition drives both in-memory mapping and IQueryable projection — with full IDE traceability.

### [ArchPillar.Extensions.Pipelines](docs/pipelines/)

A lightweight, DI-friendly, allocation-free async middleware pipeline. `Pipeline<T>` composes `IPipelineMiddleware<T>` steps around an `IPipelineHandler<T>` terminal step as pre-built nested lambdas — reusable across invocations with zero per-call allocations on the synchronous hot path. Ships with an optional `Microsoft.Extensions.DependencyInjection` integration package.

## Repository Structure

```text
├── src/
│   ├── Mapper/                            # Core mapping library
│   ├── Mapper.EntityFrameworkCore/        # EF Core integration
│   ├── Pipelines/                         # Core pipelines library
│   └── Pipelines.DependencyInjection/     # Microsoft.Extensions.DI integration
├── tests/
│   ├── Mapper.Tests/                      # Unit and integration tests
│   ├── Mapper.OData.Tests/                # OData-specific tests
│   ├── Pipelines.Tests/                   # Pipelines unit + allocation tests
│   └── Pipelines.DependencyInjection.Tests/
├── benchmarks/
│   ├── Mapper.Benchmarks/                 # Mapper BenchmarkDotNet suite
│   └── Pipelines.Benchmarks/              # Pipelines BenchmarkDotNet suite
├── samples/
│   ├── Mapper/
│   │   ├── WebShop/                       # ASP.NET Core Web API sample
│   │   └── WebShop.OData/                 # OData endpoint sample
│   └── Pipelines/
│       ├── Pipeline.BuilderSample/        # Direct (no-DI) Pipeline<T> sample
│       └── Pipeline.HostSample/           # Host-builder + AddPipeline<T>() sample
├── docs/
│   ├── mapper/                            # Mapper documentation and spec
│   └── pipelines/                         # Pipelines documentation and spec
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
