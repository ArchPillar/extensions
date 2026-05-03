using System.Diagnostics;
using ArchPillar.Extensions.Commands;
using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Primitives;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// ---------------------------------------------------------------------------
// Command.HostSample
//
// Demonstrates ArchPillar.Extensions.Commands inside a
// Microsoft.Extensions.Hosting application:
//   - Two commands: result-bearing (CreateOrder) and no-result (CancelOrder).
//   - Handlers derived from CommandHandlerBase<TCommand[, TResult]> using the
//     status factories (Ok/Created/NotFound) and assert helpers (EnsureFound).
//   - Validation per-handler via the IValidationContext fluent helpers.
//   - Implicit conversion of OperationResult to Task<OperationResult> in the
//     sync NoContent() return path.
//   - throw OperationResult — the implicit conversion to Exception unwraps to
//     OperationException, caught by ExceptionMiddleware and turned back into
//     an OperationResult.
//   - Telemetry: every dispatch produces an Activity on
//     CommandActivitySource.Name.
//   - Optional batch handler: CreateOrderBatchHandler implements
//     IBatchCommandHandler<CreateOrder, Guid> for bulk inserts.
// ---------------------------------------------------------------------------

using var listener = new ActivityListener
{
    ShouldListenTo = s => s.Name == CommandActivitySource.Name,
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    ActivityStopped = a =>
    {
        Console.WriteLine(
            $"  [activity] {a.DisplayName} status={a.Status} duration={a.Duration.TotalMilliseconds:F1}ms");
    },
};
ActivitySource.AddActivityListener(listener);

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});

builder.Services.AddSingleton<InMemoryOrderStore>();

builder.Services.AddCommands();
builder.Services.AddCommandHandler<CreateOrder, Guid, CreateOrderHandler>();
builder.Services.AddCommandHandler<CancelOrder, CancelOrderHandler>();
builder.Services.AddBatchCommandHandler<CreateOrder, Guid, CreateOrderBatchHandler>();

using IHost host = builder.Build();
host.Services.ValidateCommandRegistrations();

using IServiceScope scope = host.Services.CreateScope();
ICommandDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

Console.WriteLine("== happy path ==");
OperationResult<Guid> created = await dispatcher.SendAsync(new CreateOrder("alice", 3));
Console.WriteLine($"  status={created.Status} value={created.Value}");

Console.WriteLine();
Console.WriteLine("== validation fails ==");
OperationResult<Guid> invalid = await dispatcher.SendAsync(new CreateOrder("", 999));
Console.WriteLine($"  status={invalid.Status} errors={invalid.Errors.Count}");

Console.WriteLine();
Console.WriteLine("== cancel missing order — handler throws OperationResult.NotFound ==");
OperationResult cancel = await dispatcher.SendAsync(new CancelOrder(Guid.NewGuid()));
Console.WriteLine($"  status={cancel.Status} errors={cancel.Errors.Count}");

Console.WriteLine();
Console.WriteLine("== batch insert (3 commands, 1 invalid) ==");
CreateOrder[] batch = [new("bob", 1), new("", 0), new("carol", 2)];
IReadOnlyList<OperationResult<Guid>> batchResults = await dispatcher.SendBatchAsync<CreateOrder, Guid>(batch);
for (var i = 0; i < batchResults.Count; i++)
{
    Console.WriteLine($"  [{i}] status={batchResults[i].Status} value={batchResults[i].Value}");
}

// ---------------------------------------------------------------------------
// Domain
// ---------------------------------------------------------------------------

internal sealed record CreateOrder(string Customer, int Quantity) : ICommand<Guid>;

internal sealed record CancelOrder(Guid OrderId) : ICommand;

internal sealed class InMemoryOrderStore
{
    private readonly Dictionary<Guid, (string Customer, int Quantity)> _orders = [];

    public Guid Create(string customer, int quantity)
    {
        var id = Guid.NewGuid();
        _orders[id] = (customer, quantity);
        return id;
    }

    public bool TryGet(Guid id, out (string Customer, int Quantity) order)
        => _orders.TryGetValue(id, out order);

    public bool Remove(Guid id) => _orders.Remove(id);
}

internal sealed class CreateOrderHandler(InMemoryOrderStore store, ILogger<CreateOrderHandler> logger)
    : CommandHandlerBase<CreateOrder, Guid>
{
    public override Task ValidateAsync(CreateOrder command, IValidationContext context, CancellationToken cancellationToken)
    {
        context
            .NotEmpty(command.Customer, nameof(command.Customer))
            .Range(command.Quantity, 1, 100, nameof(command.Quantity));
        return Task.CompletedTask;
    }

    public override Task<OperationResult<Guid>> HandleAsync(CreateOrder command, CancellationToken cancellationToken)
    {
        var id = store.Create(command.Customer, command.Quantity);
        logger.LogInformation("Created order {OrderId} for {Customer}", id, command.Customer);
        return Created(id);
    }
}

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

internal sealed class CreateOrderBatchHandler(InMemoryOrderStore store, ILogger<CreateOrderBatchHandler> logger)
    : IBatchCommandHandler<CreateOrder, Guid>
{
    public Task<IReadOnlyList<OperationResult<Guid>>> HandleBatchAsync(
        IReadOnlyList<CreateOrder> commands,
        CancellationToken cancellationToken)
    {
        OperationResult<Guid>[] results = new OperationResult<Guid>[commands.Count];
        for (var i = 0; i < commands.Count; i++)
        {
            CreateOrder command = commands[i];
            var id = store.Create(command.Customer, command.Quantity);
            results[i] = OperationResult<Guid>.Created(id);
        }

        logger.LogInformation("Inserted batch of {Count} orders", commands.Count);
        return Task.FromResult<IReadOnlyList<OperationResult<Guid>>>(results);
    }
}
