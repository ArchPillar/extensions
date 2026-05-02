using System.Diagnostics;
using ArchPillar.Extensions.Primitives;
using Microsoft.Extensions.DependencyInjection;

namespace ArchPillar.Extensions.Commands.Tests;

[CollectionDefinition(nameof(CommandActivitySourceCollection), DisableParallelization = true)]
public sealed class CommandActivitySourceCollection
{
}

[Collection(nameof(CommandActivitySourceCollection))]
public class CommandActivityTests
{
    [Fact]
    public async Task Dispatch_WithListener_StartsActivityForCommandAsync()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == CommandActivitySource.Name,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a => stopped.Add(a),
        };
        ActivitySource.AddActivityListener(listener);

        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();
        ICommandDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        await dispatcher.SendAsync(new CancelOrder(Guid.NewGuid()));

        Activity activity = Assert.Single(stopped);
        Assert.Equal("Commands.CancelOrder", activity.DisplayName);
        Assert.Contains(activity.TagObjects, t => t.Key == "command.type");
    }

    [Fact]
    public async Task Dispatch_NoListener_IsPassThroughAsync()
    {
        // Sanity check: with no subscriber attached, dispatching still succeeds.
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();
        ICommandDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        OperationResult result = await dispatcher.SendAsync(new CancelOrder(Guid.NewGuid()));
        Assert.True(result.IsSuccess);
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddCommands();
        services.AddCommandHandler<CancelOrder, TestCancelOrderHandler>();
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
    }
}
