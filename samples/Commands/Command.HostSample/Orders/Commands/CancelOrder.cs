using ArchPillar.Extensions.Commands;

namespace Command.HostSample.Orders.Commands;

internal sealed record CancelOrder(Guid OrderId) : ICommand;
