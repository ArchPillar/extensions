using ArchPillar.Extensions.Commands;

namespace Command.HostSample.Orders.Commands;

internal sealed record CreateOrder(string Customer, int Quantity) : ICommand<Guid>;
