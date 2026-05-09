using ArchPillar.Extensions.Commands;
using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Operations;
using Microsoft.Extensions.Logging;

namespace Command.HostSample.Orders.Commands;

internal sealed class CreateOrderHandler(InMemoryOrderStore store, ILogger<CreateOrderHandler> logger)
    : CommandHandlerBase<CreateOrder, Guid>
{
    public override Task ValidateAsync(CreateOrder command, IValidationContext context, CancellationToken cancellationToken)
    {
        context
            .NotEmpty(command.Customer)
            .Range(command.Quantity, 1, 100);
        return Task.CompletedTask;
    }

    public override Task<OperationResult<Guid>> HandleAsync(CreateOrder command, CancellationToken cancellationToken)
    {
        var id = store.Create(command.Customer, command.Quantity);
        logger.LogInformation("Created order {OrderId} for {Customer}", id, command.Customer);
        return Created(id);
    }
}
