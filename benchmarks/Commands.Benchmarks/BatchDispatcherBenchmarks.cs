using ArchPillar.Extensions.Primitives;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace ArchPillar.Extensions.Commands.Benchmarks;

[MemoryDiagnoser]
public class BatchDispatcherBenchmarks
{
    private ServiceProvider _fanOutProvider = null!;
    private ServiceProvider _batchProvider = null!;
    private IServiceScope _fanOutScope = null!;
    private IServiceScope _batchScope = null!;
    private ICommandDispatcher _fanOutDispatcher = null!;
    private ICommandDispatcher _batchDispatcher = null!;
    private NoopCommand[] _commands = null!;

    [Params(1, 16, 256)]
    public int BatchSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var fanOut = new ServiceCollection();
        fanOut.AddCommands();
        fanOut.AddCommandHandler<NoopCommand, NoopHandler>();
        _fanOutProvider = fanOut.BuildServiceProvider();
        _fanOutScope = _fanOutProvider.CreateScope();
        _fanOutDispatcher = _fanOutScope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        var batch = new ServiceCollection();
        batch.AddCommands();
        batch.AddCommandHandler<NoopCommand, NoopHandler>();
        batch.AddBatchCommandHandler<NoopCommand, NoopBatchHandler>();
        _batchProvider = batch.BuildServiceProvider();
        _batchScope = _batchProvider.CreateScope();
        _batchDispatcher = _batchScope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        _commands = new NoopCommand[BatchSize];
        for (var i = 0; i < BatchSize; i++)
        {
            _commands[i] = new NoopCommand();
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _fanOutScope.Dispose();
        _fanOutProvider.Dispose();
        _batchScope.Dispose();
        _batchProvider.Dispose();
    }

    [Benchmark(Baseline = true)]
    public Task<IReadOnlyList<OperationResult>> SendBatchAsync_FanOutAsync()
        => _fanOutDispatcher.SendBatchAsync(_commands);

    [Benchmark]
    public Task<IReadOnlyList<OperationResult>> SendBatchAsync_BatchHandlerAsync()
        => _batchDispatcher.SendBatchAsync(_commands);

    public sealed record NoopCommand : ICommand;

    public sealed class NoopHandler : ICommandHandler<NoopCommand>
    {
        public Task<OperationResult> HandleAsync(NoopCommand command, CancellationToken cancellationToken)
            => OperationResult.Ok();
    }

    public sealed class NoopBatchHandler : IBatchCommandHandler<NoopCommand>
    {
        public Task<IReadOnlyList<OperationResult>> HandleBatchAsync(
            IReadOnlyList<NoopCommand> commands,
            CancellationToken cancellationToken)
        {
            var results = new OperationResult[commands.Count];
            for (var i = 0; i < commands.Count; i++)
            {
                results[i] = OperationResult.Ok();
            }

            return Task.FromResult<IReadOnlyList<OperationResult>>(results);
        }
    }
}
