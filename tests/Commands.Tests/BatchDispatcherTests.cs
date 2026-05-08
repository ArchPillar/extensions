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

        public Task ValidateAsync(IReadOnlyList<AddItem> commands, IValidationContext validation, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(commands);
            ArgumentNullException.ThrowIfNull(validation);

            for (var i = 0; i < commands.Count; i++)
            {
                if (string.IsNullOrEmpty(commands[i].Name))
                {
                    validation.AddError($"commands[{i}].Name", new OperationError(
                        "required",
                        $"commands[{i}].Name is required.",
                        OperationStatus.BadRequest));
                }
            }

            return Task.CompletedTask;
        }

        public Task<OperationResult> HandleBatchAsync(
            IReadOnlyList<AddItem> commands,
            CancellationToken cancellationToken)
        {
            _log.Received.AddRange(commands);
            return Task.FromResult(OperationResult.NoContent());
        }
    }

    private sealed class BatchInvocationLog
    {
        public List<AddItem> Received { get; } = [];
    }

    [Fact]
    public async Task SendBatchAsync_NoBatchHandler_FansOutAndReturnsSuccessAsync()
    {
        using ServiceProvider provider = BuildProvider(s =>
        {
            s.AddCommands();
            s.AddCommandHandler<AddItem, AddItemHandler>();
        });

        using IServiceScope scope = provider.CreateScope();
        ICommandDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        AddItem[] batch = [new("a"), new("b"), new("c")];
        OperationResult result = await dispatcher.SendBatchAsync(batch);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task SendBatchAsync_NoBatchHandler_FailFastOnFirstFailureAsync()
    {
        using ServiceProvider provider = BuildProvider(s =>
        {
            s.AddCommands();
            s.AddCommandHandler<AddItem, AddItemHandler>();
        });

        using IServiceScope scope = provider.CreateScope();
        ICommandDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        AddItem[] batch = [new("a"), new(""), new("c")];
        OperationResult result = await dispatcher.SendBatchAsync(batch);

        Assert.True(result.IsFailure);
        Assert.Equal(OperationStatus.BadRequest, result.Status);
    }

    [Fact]
    public async Task SendBatchAsync_BatchHandlerRegistered_AllValid_RunsHandlerOnFullListAsync()
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
        OperationResult result = await dispatcher.SendBatchAsync(batch);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, log.Received.Count);
        Assert.Equal(["a", "b", "c"], log.Received.Select(item => item.Name));
    }

    [Fact]
    public async Task SendBatchAsync_BatchHandlerRegistered_ValidationFails_HandlerNotInvokedAsync()
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
        OperationResult result = await dispatcher.SendBatchAsync(batch);

        Assert.True(result.IsFailure);
        Assert.Equal(OperationStatus.BadRequest, result.Status);
        Assert.NotNull(result.Problem);
        Assert.NotNull(result.Problem!.Errors);
        Assert.Contains("commands[1].Name", result.Problem.Errors!);
        Assert.Empty(log.Received);
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
        await dispatcher.SendBatchAsync(batch);

        Assert.Equal(1, middleware.Invocations);
        Assert.Equal(3, middleware.LastBatchSize);
    }

    [Fact]
    public async Task SendBatchAsync_NoBatchHandler_RunsThroughPipelineOnceWithBatchContextAsync()
    {
        // Batch dispatch always runs through the pipeline once — even
        // without a registered batch handler. The router takes care of the
        // per-item iteration internally, so wrapping middleware sees a
        // single batch context covering the whole group.
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

        Assert.Equal(1, middleware.Invocations);
        Assert.Equal(3, middleware.LastBatchSize);
    }

    [Fact]
    public async Task SendBatchAsync_EmptyBatch_ReturnsSuccessAsync()
    {
        using ServiceProvider provider = BuildProvider(s =>
        {
            s.AddCommands();
            s.AddCommandHandler<AddItem, AddItemHandler>();
        });

        using IServiceScope scope = provider.CreateScope();
        ICommandDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        OperationResult result = await dispatcher.SendBatchAsync(Array.Empty<AddItem>());

        Assert.True(result.IsSuccess);
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
