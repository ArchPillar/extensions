using ArchPillar.Extensions.Operations;

namespace ArchPillar.Extensions.Commands.Internal;

/// <summary>
/// Captures the batch call site for a command at registration time. Stored
/// separately from <see cref="CommandInvokerDescriptor"/> so that adding a
/// batch handler does not need to read or modify existing descriptor
/// registrations (which would trigger recursive service resolution).
/// </summary>
internal sealed class BatchInvokerEntry
{
    public BatchInvokerEntry(
        Type commandType,
        Func<IServiceProvider, IReadOnlyList<IRequest>, CancellationToken, Task<IReadOnlyList<OperationResult>>> invokeBatchAsync)
    {
        CommandType = commandType;
        InvokeBatchAsync = invokeBatchAsync;
    }

    public Type CommandType { get; }

    public Func<IServiceProvider, IReadOnlyList<IRequest>, CancellationToken, Task<IReadOnlyList<OperationResult>>> InvokeBatchAsync { get; }
}
