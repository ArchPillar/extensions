using BenchmarkDotNet.Running;

namespace ArchPillar.Extensions.Localization.Benchmarks;

// Run with: dotnet run -c Release --project benchmarks/Localization.Benchmarks
// An explicit, namespaced entry point (rather than top-level statements) so this assembly's Program does not
// collide with the referenced Tooling's own top-level Program.
internal static class Program
{
    private static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
