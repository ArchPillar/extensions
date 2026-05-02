using ArchPillar.Extensions.Commands;
using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Primitives;

namespace ArchPillar.Extensions.Commands.Tests;

internal sealed record CreateOrder(string CustomerId, int Quantity) : ICommand<Guid>;

internal sealed record CancelOrder(Guid OrderId) : ICommand;

internal sealed record AlwaysThrow(string Message) : ICommand;

internal sealed class TestCreateOrderHandler : CommandHandlerBase<CreateOrder, Guid>
{
    public Guid LastCreated { get; private set; }

    public override Task ValidateAsync(CreateOrder command, IValidationContext context, CancellationToken cancellationToken)
    {
        context
            .NotEmpty(command.CustomerId, nameof(command.CustomerId))
            .Range(command.Quantity, 1, 100, nameof(command.Quantity));
        return Task.CompletedTask;
    }

    public override Task<OperationResult<Guid>> HandleAsync(CreateOrder command, CancellationToken cancellationToken)
    {
        LastCreated = Guid.NewGuid();
        return Created(LastCreated);
    }
}

internal sealed class TestCancelOrderHandler : CommandHandlerBase<CancelOrder>
{
    public Guid? LastCancelled { get; private set; }

    public override Task ValidateAsync(CancelOrder command, IValidationContext context, CancellationToken cancellationToken)
    {
        context.Must(command.OrderId != Guid.Empty, "required", "OrderId is required.", nameof(command.OrderId));
        return Task.CompletedTask;
    }

    public override Task<OperationResult> HandleAsync(CancelOrder command, CancellationToken cancellationToken)
    {
        LastCancelled = command.OrderId;
        return NoContent();
    }
}

internal sealed class AlwaysThrowHandler : CommandHandlerBase<AlwaysThrow>
{
    public override Task ValidateAsync(AlwaysThrow command, IValidationContext context, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public override Task<OperationResult> HandleAsync(AlwaysThrow command, CancellationToken cancellationToken)
        => throw new InvalidOperationException(command.Message);
}
