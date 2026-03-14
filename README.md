# ArchPillar Extensions

A collection of standalone .NET libraries that grew out of personal needs during real-world projects. Published in case they help anyone else. :)

## Libraries

### [ArchPillar.Extensions.Mapper](docs/mapper/)

Explicit object-to-object DTO mapping and LINQ/EF Core expression projection. One definition drives both in-memory mapping and IQueryable projection — with full IDE traceability.

## Repository Structure

```text
├── src/
│   └── Mapper/                    # Core mapping library
├── tests/
│   ├── Mapper.Tests/              # Unit and integration tests
│   └── Mapper.OData.Tests/        # OData-specific tests
├── benchmarks/
│   └── Mapper.Benchmarks/         # BenchmarkDotNet performance tests
├── samples/
│   └── Mapper/
│       ├── WebShop/               # ASP.NET Core Web API sample
│       └── WebShop.OData/         # OData endpoint sample
├── docs/
│   └── mapper/                    # Library documentation and spec
├── Directory.Build.props          # Shared project properties
├── Directory.Build.targets        # Roslyn analyzer configuration
├── Directory.Packages.props       # Central package management
└── ArchPillar.Extensions.slnx     # Solution file
```

> **Note on samples:** The sample projects exist solely to demonstrate these libraries in action. They are not intended as guidance on how to structure services or products.

## Requirements

- .NET 8, 9, or 10
- C# 14

## License

[MIT](LICENSE)
