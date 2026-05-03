using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace ArchPillar.Extensions.Commands.Tests;

public class BatchDispatcherTests
{
    private sealed record AddItem(string Name) : ICommand;

    private sealed class AddItemHandler : CommandHandlerBase<AddItem>
    {
        public override Task ValidateAsync(AddItem command, IValidationContext context, CancellationToken cancellationToken)
        {
            context.NotEmpty(command.Name);
            return Task.CompletedTask;
        }

        public override Task<OperationResult> HandleAsync(AddItem command, CancellationToken cancellationToken)
            => NoContent();
    }

    private sealed class TrackingBatchHandler : IBatchCommandHandler<AddItem>
    {
        private readonly BatchInvocationLog _log;

        public TrackingBatchHandler(BatchInvocationLog log)
        {
            _log = log;
        }

        public Task<IReadOnlyList<OperationResult>> HandleBatchAsync(
            IReadOnlyList<AddItem> commands,
            CancellationToken cancellationToken)
        {
            _log.Received.AddRange(commands);
            var results = new OperationResult[commands.Count];
            for (var i = 0; i < commands.Count; i++)
            {
                results[i] = OperationResult.NoContent();
            }

            return Task.FromResult<IReadOnlyList<OperationResult>>(results);
        }
    }

    private sealed class BatchInvocationLog
    {
        public List<AddItem> Received { get; } = [];
    }

    [Fact]
    public async Task SendBatchAsync_NoBatchHandler_FansOutAsync()
    {
        using ServiceProvider provider = BuildProvider(s =>
        {
            s.AddCommands();
            s.AddCommandHandler<AddItem, AddItemHandler>();
        });

        using IServiceScope scope = provider.CreateScope();
        ICommandDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        AddItem[] batch = [new("a"), new("b"), new("c")];
        IReadOnlyList<OperationResult> results = await dispatcher.SendBatchAsync(batch);

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal(OperationStatus.NoContent, r.Status));
    }

    [Fact]
    public async Task SendBatchAsync_WithBatchHandler_UsesBatchHandlerAsync()
    {
        var log = new BatchInvocationLog();

        using ServiceProvider provider = BuildProvider(s =>
        {
            s.AddSingleton(log);
            s.AddCommands();
            s.AddCommandHandler<AddItem, AddItemHandler>();
            s.AddBatchCommandHandler<AddItem, TrackingBatchHandler>();
        });

        using IServiceScope scope = provider.CreateScope();
        ICommandDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        AddItem[] batch = [new("a"), new("b")];
        IReadOnlyList<OperationResult> results = await dispatcher.SendBatchAsync(batch);

        Assert.Equal(2, results.Count);
        Assert.Equal(2, log.Received.Count);
    }

    [Fact]
    public async Task SendBatchAsync_FiltersInvalidBeforeBatchCallAsync()
    {
        var log = new BatchInvocationLog();

        using ServiceProvider provider = BuildProvider(s =>
        {
            s.AddSingleton(log);
            s.AddCommands();
            s.AddCommandHandler<AddItem, AddItemHandler>();
            s.AddBatchCommandHandler<AddItem, TrackingBatchHandler>();
        });

        using IServiceScope scope = provider.CreateScope();
        ICommandDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        AddItem[] batch = [new("ok"), new(""), new("also-ok")];
        IReadOnlyList<OperationResult> results = await dispatcher.SendBatchAsync(batch);

        Assert.Equal(3, results.Count);
        Assert.Equal(OperationStatus.NoContent, results[0].Status);
        Assert.Equal(OperationStatus.BadRequest, results[1].Status);
        Assert.Equal(OperationStatus.NoContent, results[2].Status);

        Assert.Equal(2, log.Received.Count);
        Assert.Contains(log.Received, c => c.Name == "ok");
        Assert.Contains(log.Received, c => c.Name == "also-ok");
    }

    [Fact]
    public async Task SendBatchAsync_EmptyBatch_ReturnsEmptyAsync()
    {
        using ServiceProvider provider = BuildProvider(s =>
        {
            s.AddCommands();
            s.AddCommandHandler<AddItem, AddItemHandler>();
        });

        using IServiceScope scope = provider.CreateScope();
        ICommandDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        IReadOnlyList<OperationResult> results = await dispatcher.SendBatchAsync(Array.Empty<AddItem>());

        Assert.Empty(results);
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
    }
}
