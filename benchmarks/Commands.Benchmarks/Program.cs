using BenchmarkDotNet.Running;

// Run with: dotnet run -c Release --project benchmarks/Commands.Benchmarks
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
