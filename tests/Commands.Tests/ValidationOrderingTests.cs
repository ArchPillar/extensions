using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Pipelines;
using ArchPillar.Extensions.Primitives;
using Microsoft.Extensions.DependencyInjection;

namespace ArchPillar.Extensions.Commands.Tests;

public class ValidationOrderingTests
{
    private sealed record DoThing(string Value) : ICommand;

    private sealed class DoThingHandler : CommandHandlerBase<DoThing>
    {
        private readonly OrderingLog _log;

        public DoThingHandler(OrderingLog log)
        {
            _log = log;
        }

        public override Task ValidateAsync(DoThing command, IValidationContext context, CancellationToken cancellationToken)
        {
            _log.Events.Add("validate");
            context.NotEmpty(command.Value, nameof(command.Value));
            return Task.CompletedTask;
        }

        public override Task<OperationResult> HandleAsync(DoThing command, CancellationToken cancellationToken)
        {
            _log.Events.Add("handle");
            return NoContent();
        }
    }

    private sealed class TracingMiddleware : IPipelineMiddleware<CommandContext>
    {
        private readonly OrderingLog _log;

        public TracingMiddleware(OrderingLog log)
        {
            _log = log;
        }

        public async Task InvokeAsync(CommandContext context, PipelineDelegate<CommandContext> next, CancellationToken cancellationToken = default)
        {
            _log.Events.Add("user-middleware:before");
            await next(context, cancellationToken);
            _log.Events.Add("user-middleware:after");
        }
    }

    private sealed class OrderingLog
    {
        public List<string> Events { get; } = [];
    }

    [Fact]
    public async Task UserMiddleware_WrapsValidationAndHandlerAsync()
    {
        var log = new OrderingLog();

        using ServiceProvider provider = BuildProvider(s =>
        {
            s.AddSingleton(log);
            s.AddCommands();
            s.AddCommandHandler<DoThing, DoThingHandler>();
            s.AddPipelineMiddleware<CommandContext, TracingMiddleware>();
        });

        using IServiceScope scope = provider.CreateScope();
        ICommandDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        OperationResult result = await dispatcher.SendAsync(new DoThing("ok"));

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["user-middleware:before", "validate", "handle", "user-middleware:after"],
            log.Events);
    }

    [Fact]
    public async Task UserMiddleware_WrapsValidationOnFailure_TooAsync()
    {
        var log = new OrderingLog();

        using ServiceProvider provider = BuildProvider(s =>
        {
            s.AddSingleton(log);
            s.AddCommands();
            s.AddCommandHandler<DoThing, DoThingHandler>();
            s.AddPipelineMiddleware<CommandContext, TracingMiddleware>();
        });

        using IServiceScope scope = provider.CreateScope();
        ICommandDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        OperationResult result = await dispatcher.SendAsync(new DoThing(""));

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationStatus.UnprocessableEntity, result.Status);

        // Validation runs inside the user middleware; the handler does not.
        Assert.Equal(
            ["user-middleware:before", "validate", "user-middleware:after"],
            log.Events);
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
