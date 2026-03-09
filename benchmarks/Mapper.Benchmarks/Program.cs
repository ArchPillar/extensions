using BenchmarkDotNet.Running;

// Run with: dotnet run -c Release --project benchmarks/Mapper.Benchmarks
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
