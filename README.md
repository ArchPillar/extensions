# ArchPillar Extensions

A collection of standalone .NET libraries that grew out of personal needs during real-world projects. Published in case they help anyone else. :)

## Libraries

### [ArchPillar.Extensions.Mapper](docs/mapper/)

Explicit object-to-object DTO mapping and LINQ/EF Core expression projection. One definition drives both in-memory mapping and IQueryable projection — with full IDE traceability.

### [ArchPillar.Extensions.Primitives](docs/primitives/)

A collection of small, dependency-free .NET primitives. Currently ships `Pipeline<T>`, a lightweight DI-friendly async middleware pipeline built on pre-composed nested lambdas. Allocation-free on the synchronous hot path. Ships with an optional `Microsoft.Extensions.DependencyInjection` integration package.

## Repository Structure

```text
├── src/
│   ├── Mapper/                            # Core mapping library
│   ├── Mapper.EntityFrameworkCore/        # EF Core integration
│   └── Primitives/                        # Core primitives library
│       └── DependencyInjection/           # Microsoft.Extensions.DI integration
├── tests/
│   ├── Mapper.Tests/                      # Unit and integration tests
│   ├── Mapper.OData.Tests/                # OData-specific tests
│   ├── Primitives.Tests/                  # Primitives unit + allocation tests
│   └── Primitives.DependencyInjection.Tests/
├── benchmarks/
│   ├── Mapper.Benchmarks/                 # Mapper BenchmarkDotNet suite
│   └── Primitives.Benchmarks/             # Primitives BenchmarkDotNet suite
├── samples/
│   ├── Mapper/
│   │   ├── WebShop/                       # ASP.NET Core Web API sample
│   │   └── WebShop.OData/                 # OData endpoint sample
│   └── Primitives/
│       ├── Pipeline.BuilderSample/        # Direct (no-DI) Pipeline<T> sample
│       └── Pipeline.HostSample/           # Host-builder + AddPipeline<T>() sample
├── docs/
│   ├── mapper/                            # Mapper documentation and spec
│   └── primitives/                        # Primitives documentation and spec
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
