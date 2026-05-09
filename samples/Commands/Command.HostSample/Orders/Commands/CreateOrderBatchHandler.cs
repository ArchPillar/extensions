using ArchPillar.Extensions.Commands;
using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Operations;
using Microsoft.Extensions.Logging;

namespace Command.HostSample.Orders.Commands;

internal sealed class CreateOrderBatchHandler(InMemoryOrderStore store, ILogger<CreateOrderBatchHandler> logger)
    : IBatchCommandHandler<CreateOrder, Guid>
{
    public Task ValidateAsync(
        IReadOnlyList<CreateOrder> commands,
        IValidationContext validation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(validation);

        for (var i = 0; i < commands.Count; i++)
        {
            CreateOrder command = commands[i];
            validation
                .NotEmpty(command.Customer, field: $"commands[{i}].Customer")
                .Range(command.Quantity, 1, 100, field: $"commands[{i}].Quantity");
        }

        return Task.CompletedTask;
    }

    public Task<OperationResult<IReadOnlyList<Guid>>> HandleBatchAsync(
        IReadOnlyList<CreateOrder> commands,
        CancellationToken cancellationToken)
    {
        var ids = new Guid[commands.Count];
        for (var i = 0; i < commands.Count; i++)
        {
            CreateOrder command = commands[i];
            ids[i] = store.Create(command.Customer, command.Quantity);
        }

        logger.LogInformation("Inserted batch of {Count} orders", commands.Count);
        return Task.FromResult(OperationResult.Ok<IReadOnlyList<Guid>>(ids));
    }
}
