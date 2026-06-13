using ArchPillar.Extensions.Commands;

namespace Commands.HostSample.Orders.Commands;

internal sealed record CancelOrder(Guid OrderId) : ICommand;
