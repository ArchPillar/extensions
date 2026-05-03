using ArchPillar.Extensions.Primitives;
using Microsoft.Extensions.DependencyInjection;

namespace ArchPillar.Extensions.Commands.Tests;

public class CommandDispatcherTests
{
    [Fact]
    public async Task SendAsync_ResultBearingCommand_ReturnsCreatedAsync()
    {
        ServiceProvider provider = BuildProvider(s =>
        {
            s.AddCommands();
            s.AddCommandHandler<CreateOrder, Guid, TestCreateOrderHandler>();
        });

        using IServiceScope scope = provider.CreateScope();
        ICommandDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        OperationResult<Guid> result = await dispatcher.SendAsync(new CreateOrder("cust-1", 3));

        Assert.True(result.IsSuccess);
        Assert.Equal(OperationStatus.Created, result.Status);
        Assert.NotEqual(Guid.Empty, result.Value);
    }

    [Fact]
    public async Task SendAsync_NoResultCommand_ReturnsNoContentAsync()
    {
        ServiceProvider provider = BuildProvider(s =>
        {
            s.AddCommands();
            s.AddCommandHandler<CancelOrder, TestCancelOrderHandler>();
        });

        using IServiceScope scope = provider.CreateScope();
        ICommandDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        OperationResult result = await dispatcher.SendAsync(new CancelOrder(Guid.NewGuid()));

        Assert.True(result.IsSuccess);
        Assert.Equal(OperationStatus.NoContent, result.Status);
    }

    [Fact]
    public async Task SendAsync_ValidationFails_ReturnsUnprocessableEntityAsync()
    {
        ServiceProvider provider = BuildProvider(s =>
        {
            s.AddCommands();
            s.AddCommandHandler<CreateOrder, Guid, TestCreateOrderHandler>();
        });

        using IServiceScope scope = provider.CreateScope();
        ICommandDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        OperationResult<Guid> result = await dispatcher.SendAsync(new CreateOrder("", 999));

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationStatus.BadRequest, result.Status);
        Assert.NotNull(result.Problem);
        Assert.NotNull(result.Problem!.Errors);
        Assert.Equal(2, result.Problem.Errors!.Count);
    }

    [Fact]
    public async Task SendAsync_HandlerThrows_ReturnsInternalServerErrorAsync()
    {
        ServiceProvider provider = BuildProvider(s =>
        {
            s.AddCommands();
            s.AddCommandHandler<AlwaysThrow, AlwaysThrowHandler>();
        });

        using IServiceScope scope = provider.CreateScope();
        ICommandDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        OperationResult result = await dispatcher.SendAsync(new AlwaysThrow("boom"));

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationStatus.InternalServerError, result.Status);
        InvalidOperationException ex = Assert.IsType<InvalidOperationException>(result.Exception);
        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public async Task SendAsync_HandlerThrowsOperationException_UnwrapsResultAsync()
    {
        ServiceProvider provider = BuildProvider(s =>
        {
            s.AddCommands();
            s.AddCommandHandler<CancelOrder, ThrowingCancelHandler>();
        });

        using IServiceScope scope = provider.CreateScope();
        ICommandDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        OperationResult result = await dispatcher.SendAsync(new CancelOrder(Guid.NewGuid()));

        Assert.False(result.IsSuccess);
        Assert.Equal(OperationStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task SendAsync_NoHandlerRegistered_ReturnsInternalServerErrorAsync()
    {
        ServiceProvider provider = BuildProvider(s => s.AddCommands());

        using IServiceScope scope = provider.CreateScope();
        ICommandDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        OperationResult result = await dispatcher.SendAsync(new CancelOrder(Guid.NewGuid()));

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Exception);
    }

    [Fact]
    public async Task SendAsync_NullCommand_ThrowsAsync()
    {
        ServiceProvider provider = BuildProvider(s =>
        {
            s.AddCommands();
            s.AddCommandHandler<CancelOrder, TestCancelOrderHandler>();
        });

        using IServiceScope scope = provider.CreateScope();
        ICommandDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        await Assert.ThrowsAsync<ArgumentNullException>(() => dispatcher.SendAsync((ICommand)null!));
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

    private sealed class ThrowingCancelHandler : CommandHandlerBase<CancelOrder>
    {
        public override Task ValidateAsync(CancelOrder command, Validation.IValidationContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public override Task<OperationResult> HandleAsync(CancelOrder command, CancellationToken cancellationToken)
        {
            EnsureFound<object>(null, "Order missing");
            return Ok();
        }
    }
}
