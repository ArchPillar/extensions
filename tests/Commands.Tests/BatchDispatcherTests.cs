using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Operations;
using ArchPillar.Extensions.Pipelines;
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
    public async Task SendBatchAsync_AnyValidationFails_RejectsWholeBatchAsync()
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
        // Items that would have passed get PreconditionFailed/batch_rejected,
        // distinguishable from the item that failed its own validation.
        Assert.Equal(OperationStatus.PreconditionFailed, results[0].Status);
        Assert.Equal("batch_rejected", results[0].Problem?.Type);
        Assert.Equal(OperationStatus.BadRequest, results[1].Status);
        Assert.NotEqual("batch_rejected", results[1].Problem?.Type);
        Assert.Equal(OperationStatus.PreconditionFailed, results[2].Status);
        Assert.Equal("batch_rejected", results[2].Problem?.Type);

        Assert.Empty(log.Received);
    }

    [Fact]
    public async Task SendBatchAsync_AllValid_HandlerReceivesFullListAsync()
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

        AddItem[] batch = [new("a"), new("b"), new("c")];
        IReadOnlyList<OperationResult> results = await dispatcher.SendBatchAsync(batch);

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal(OperationStatus.NoContent, r.Status));
        Assert.Equal(3, log.Received.Count);
        Assert.Equal(["a", "b", "c"], log.Received.Select(item => item.Name));
    }

    private sealed class CountingMiddleware : IPipelineMiddleware<CommandContext>
    {
        public int Invocations { get; private set; }
        public int? LastBatchSize { get; private set; }

        public async Task InvokeAsync(CommandContext context, PipelineDelegate<CommandContext> next, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(next);

            Invocations++;
            LastBatchSize = context.BatchCommands?.Count;
            await next(context, cancellationToken).ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task SendBatchAsync_BatchHandlerRegistered_RunsThroughPipelineOnceAsync()
    {
        var middleware = new CountingMiddleware();
        var log = new BatchInvocationLog();

        using ServiceProvider provider = BuildProvider(s =>
        {
            s.AddSingleton(log);
            s.AddCommands();
            s.AddCommandHandler<AddItem, AddItemHandler>();
            s.AddBatchCommandHandler<AddItem, TrackingBatchHandler>();
            s.AddSingleton<IPipelineMiddleware<CommandContext>>(middleware);
        });

        using IServiceScope scope = provider.CreateScope();
        ICommandDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        AddItem[] batch = [new("a"), new("b"), new("c")];
        IReadOnlyList<OperationResult> results = await dispatcher.SendBatchAsync(batch);

        Assert.Equal(3, results.Count);
        Assert.Equal(1, middleware.Invocations);
        Assert.Equal(3, middleware.LastBatchSize);
    }

    [Fact]
    public async Task SendBatchAsync_NoBatchHandler_FansOutThroughPipelinePerCommandAsync()
    {
        var middleware = new CountingMiddleware();

        using ServiceProvider provider = BuildProvider(s =>
        {
            s.AddCommands();
            s.AddCommandHandler<AddItem, AddItemHandler>();
            s.AddSingleton<IPipelineMiddleware<CommandContext>>(middleware);
        });

        using IServiceScope scope = provider.CreateScope();
        ICommandDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        AddItem[] batch = [new("a"), new("b"), new("c")];
        await dispatcher.SendBatchAsync(batch);

        Assert.Equal(3, middleware.Invocations);
        Assert.Null(middleware.LastBatchSize);
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
