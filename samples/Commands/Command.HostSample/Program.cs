using System.Diagnostics;
using ArchPillar.Extensions.Commands;
using ArchPillar.Extensions.Operations;
using Command.HostSample.Orders;
using Command.HostSample.Orders.Commands;
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
//
// Domain types live under Orders/ — one file per class.
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
Console.WriteLine($"  status={invalid.Status} fields={invalid.Problem?.Errors?.Count ?? 0}");

Console.WriteLine();
Console.WriteLine("== cancel missing order — handler throws OperationResult.NotFound ==");
OperationResult cancel = await dispatcher.SendAsync(new CancelOrder(Guid.NewGuid()));
Console.WriteLine($"  status={cancel.Status} detail={cancel.Problem?.Detail}");

Console.WriteLine();
Console.WriteLine("== batch insert (3 commands, 1 invalid) ==");
CreateOrder[] batch = [new("bob", 1), new("", 0), new("carol", 2)];
IReadOnlyList<OperationResult<Guid>> batchResults = await dispatcher.SendBatchAsync<CreateOrder, Guid>(batch);
for (var i = 0; i < batchResults.Count; i++)
{
    Console.WriteLine($"  [{i}] status={batchResults[i].Status} value={batchResults[i].Value}");
}
