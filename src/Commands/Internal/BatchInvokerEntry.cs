using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Operations;

namespace ArchPillar.Extensions.Commands.Internal;

internal sealed class BatchInvokerEntry
{
    public BatchInvokerEntry(
        Type commandType,
        Func<IServiceProvider, IReadOnlyList<IRequest>, IValidationContext, CancellationToken, Task> validateBatchAsync,
        Func<IServiceProvider, IReadOnlyList<IRequest>, CancellationToken, Task<OperationResult>> invokeBatchAsync)
    {
        CommandType = commandType;
        ValidateBatchAsync = validateBatchAsync;
        InvokeBatchAsync = invokeBatchAsync;
    }

    public Type CommandType { get; }

    public Func<IServiceProvider, IReadOnlyList<IRequest>, IValidationContext, CancellationToken, Task> ValidateBatchAsync { get; }

    public Func<IServiceProvider, IReadOnlyList<IRequest>, CancellationToken, Task<OperationResult>> InvokeBatchAsync { get; }
}
