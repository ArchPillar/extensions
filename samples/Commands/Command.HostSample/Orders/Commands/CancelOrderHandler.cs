using ArchPillar.Extensions.Commands;
using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Operations;
using Microsoft.Extensions.Logging;

namespace Command.HostSample.Orders.Commands;

internal sealed class CancelOrderHandler(InMemoryOrderStore store, ILogger<CancelOrderHandler> logger)
    : CommandHandlerBase<CancelOrder>
{
    public override Task ValidateAsync(CancelOrder command, IValidationContext context, CancellationToken cancellationToken)
    {
        context.Must(command.OrderId != Guid.Empty, "required", "OrderId is required.", nameof(command.OrderId));
        return Task.CompletedTask;
    }

    public override Task<OperationResult> HandleAsync(CancelOrder command, CancellationToken cancellationToken)
    {
        if (!store.TryGet(command.OrderId, out _))
        {
            // Implicit conversion: OperationResult -> Exception (OperationException carrying the result).
            // ExceptionMiddleware unwraps it back into the dispatcher's returned result.
            throw OperationResult.NotFound($"Order {command.OrderId} not found.");
        }

        store.Remove(command.OrderId);
        logger.LogInformation("Cancelled order {OrderId}", command.OrderId);
        return NoContent();
    }
}
