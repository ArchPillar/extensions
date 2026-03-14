# Contributing

Thanks for your interest in contributing! This is a small personal project, so the process is kept simple.

## Getting Started

1. Fork the repository and clone your fork
2. Make sure you have the [.NET 10 SDK](https://dotnet.microsoft.com/download) installed (the build also targets .NET 8 and 9)
3. Run the build and tests to verify your setup:

```bash
dotnet build
dotnet test tests/Mapper.Tests
```

## Making Changes

- Open an issue first to discuss non-trivial changes — this avoids wasted effort if the change doesn't fit the project's direction.
- Small bug fixes and documentation improvements can go straight to a pull request.
- Keep pull requests focused on a single change. Separate unrelated fixes into their own PRs.

## Code Style

The repository enforces code style through `.editorconfig` and Roslyn analyzers. A clean build in Release mode (`dotnet build -c Release`) must produce zero warnings. The CI pipeline will catch any violations, but catching them locally is faster.

Key conventions:

- File-scoped namespaces, nullable reference types enabled
- Fields use `_camelCase`, constants use `PascalCase`
- Seal classes that are not designed for inheritance
- XML doc comments on all public members, none on internal/private

See the `.editorconfig` for the full set of rules.

## Pull Request Process

1. Make sure `dotnet build -c Release` and `dotnet test tests/Mapper.Tests` pass locally
2. Write or update tests for your changes
3. Open a pull request against `main` with a clear description of what and why
4. CI runs automatically on all pull requests — all checks must pass before merge

## Reporting Issues

Use [GitHub Issues](https://github.com/ArchPillar/extensions/issues). Include:

- What you expected to happen
- What actually happened
- A minimal code sample that reproduces the problem
- Submitting a PR with a failing test that demonstrates the problem is a major advantage

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE).
