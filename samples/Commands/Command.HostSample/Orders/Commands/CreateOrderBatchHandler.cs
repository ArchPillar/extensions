using ArchPillar.Extensions.Commands;
using ArchPillar.Extensions.Operations;
using Microsoft.Extensions.Logging;

namespace Command.HostSample.Orders.Commands;

internal sealed class CreateOrderBatchHandler(InMemoryOrderStore store, ILogger<CreateOrderBatchHandler> logger)
    : IBatchCommandHandler<CreateOrder, Guid>
{
    public Task<IReadOnlyList<OperationResult<Guid>>> HandleBatchAsync(
        IReadOnlyList<CreateOrder> commands,
        CancellationToken cancellationToken)
    {
        var results = new OperationResult<Guid>[commands.Count];
        for (var i = 0; i < commands.Count; i++)
        {
            CreateOrder command = commands[i];
            var id = store.Create(command.Customer, command.Quantity);
            results[i] = OperationResult.Created(id);
        }

        logger.LogInformation("Inserted batch of {Count} orders", commands.Count);
        return Task.FromResult<IReadOnlyList<OperationResult<Guid>>>(results);
    }
}
