using ArchPillar.Extensions.Commands.Internal;
using ArchPillar.Extensions.Pipelines;
using Microsoft.Extensions.DependencyInjection;

namespace ArchPillar.Extensions.Commands;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCommands_RegistersDispatcher_AndPipeline()
    {
        var services = new ServiceCollection();
        services.AddCommands();
        services.AddCommandHandler<CancelOrder, TestCancelOrderHandler>();

        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        using IServiceScope scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService<ICommandDispatcher>());
        Assert.NotNull(scope.ServiceProvider.GetService<Pipeline<CommandContext>>());
    }

    [Fact]
    public void AddCommands_CalledTwice_DoesNotDoubleRegisterMiddlewares()
    {
        var services = new ServiceCollection();
        services.AddCommands();
        services.AddCommands();
        services.AddCommandHandler<CancelOrder, TestCancelOrderHandler>();

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        IEnumerable<IPipelineMiddleware<CommandContext>> middlewares =
            scope.ServiceProvider.GetServices<IPipelineMiddleware<CommandContext>>();

        // Built-ins: ActivityMiddleware + ExceptionMiddleware. Validation runs
        // inside the router, not as a middleware, so it doesn't appear here.
        Assert.Equal(2, middlewares.Count());
    }

    [Fact]
    public void ValidateCommandRegistrations_NoHandlers_Passes()
    {
        var services = new ServiceCollection();
        services.AddCommands();
        using ServiceProvider provider = services.BuildServiceProvider();

        // No handlers — nothing to validate, but the call should succeed.
        provider.ValidateCommandRegistrations();
    }

    [Fact]
    public void Registry_LookupResolvesLazily()
    {
        var services = new ServiceCollection();
        services.AddCommands();
        services.AddCommandHandler<CancelOrder, TestCancelOrderHandler>();

        using ServiceProvider provider = services.BuildServiceProvider();
        CommandInvokerRegistry registry = provider.GetRequiredService<CommandInvokerRegistry>();

        Assert.True(registry.TryGet(typeof(CancelOrder), out CommandInvokerDescriptor d));
        Assert.Equal(typeof(CancelOrder), d.CommandType);

        Assert.False(registry.TryGet(typeof(CreateOrder), out _));
    }
}
