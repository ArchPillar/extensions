using ArchPillar.Extensions.Primitives;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace ArchPillar.Extensions.Commands.Benchmarks;

[MemoryDiagnoser]
public class CommandDispatcherBenchmarks
{
    private ServiceProvider _provider = null!;
    private IServiceScope _scope = null!;
    private ICommandDispatcher _dispatcher = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddCommands();
        services.AddCommandHandler<NoopCommand, NoopHandler>();
        services.AddCommandHandler<NoopValueCommand, int, NoopValueHandler>();

        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
        _dispatcher = _scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _scope.Dispose();
        _provider.Dispose();
    }

    [Benchmark]
    public Task<OperationResult> SendAsync_FireAndForgetAsync()
        => _dispatcher.SendAsync(new NoopCommand());

    [Benchmark]
    public Task<OperationResult<int>> SendAsync_ResultBearingAsync()
        => _dispatcher.SendAsync(new NoopValueCommand());

    public sealed record NoopCommand : ICommand;

    public sealed record NoopValueCommand : ICommand<int>;

    public sealed class NoopHandler : ICommandHandler<NoopCommand>
    {
        public Task<OperationResult> HandleAsync(NoopCommand command, CancellationToken cancellationToken)
            => OperationResult.Ok();
    }

    public sealed class NoopValueHandler : ICommandHandler<NoopValueCommand, int>
    {
        public Task<OperationResult<int>> HandleAsync(NoopValueCommand command, CancellationToken cancellationToken)
            => OperationResult<int>.Ok(42);
    }
}
