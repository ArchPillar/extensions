using System.Diagnostics;
using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace ArchPillar.Extensions.Commands.Tests;

public class CommandActivityTests
{
    // Uniquely-named so other tests dispatching CancelOrder / CreateOrder /
    // AddItem in parallel don't add their activities to the listener buffer.
    private sealed record TelemetryProbe : ICommand;

    private sealed class TelemetryProbeHandler : CommandHandlerBase<TelemetryProbe>
    {
        public override Task ValidateAsync(TelemetryProbe command, IValidationContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public override Task<OperationResult> HandleAsync(TelemetryProbe command, CancellationToken cancellationToken)
            => NoContent();
    }

    [Fact]
    public async Task Dispatch_WithListener_StartsActivityForCommandAsync()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == CommandActivitySource.Name,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a =>
            {
                if (a.DisplayName == "Commands.TelemetryProbe")
                {
                    stopped.Add(a);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();
        ICommandDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        await dispatcher.SendAsync(new TelemetryProbe());

        Activity activity = Assert.Single(stopped);
        Assert.Equal("Commands.TelemetryProbe", activity.DisplayName);
        Assert.Contains(activity.TagObjects, t => t.Key == "command.type");
    }

    [Fact]
    public async Task Dispatch_NoListener_IsPassThroughAsync()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();
        ICommandDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        OperationResult result = await dispatcher.SendAsync(new TelemetryProbe());
        Assert.True(result.IsSuccess);
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddCommands();
        services.AddCommandHandler<TelemetryProbe, TelemetryProbeHandler>();
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
    }
}
