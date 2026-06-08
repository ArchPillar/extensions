using ArchPillar.Extensions.Commands;

namespace Commands.HostSample.Orders.Commands;

internal sealed record CreateOrder(string Customer, int Quantity) : ICommand<Guid>;
