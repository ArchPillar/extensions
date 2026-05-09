using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Operations;

namespace ArchPillar.Extensions.Commands.Internal;

internal sealed class CommandInvokerDescriptor
{
    public CommandInvokerDescriptor(
        Type commandType,
        bool producesResult,
        Func<IServiceProvider, IRequest, IValidationContext, CancellationToken, Task> validateAsync,
        Func<IServiceProvider, IRequest, CancellationToken, Task<OperationResult>> invokeAsync,
        Action<IServiceProvider> resolveHandler,
        Func<IServiceProvider, IReadOnlyList<IRequest>, IValidationContext, CancellationToken, Task>? validateBatchAsync = null,
        Func<IServiceProvider, IReadOnlyList<IRequest>, CancellationToken, Task<OperationResult>>? invokeBatchAsync = null)
    {
        CommandType = commandType;
        ProducesResult = producesResult;
        ValidateAsync = validateAsync;
        InvokeAsync = invokeAsync;
        ResolveHandler = resolveHandler;
        ValidateBatchAsync = validateBatchAsync;
        InvokeBatchAsync = invokeBatchAsync;
    }

    public Type CommandType { get; }

    public bool ProducesResult { get; }

    public Func<IServiceProvider, IRequest, IValidationContext, CancellationToken, Task> ValidateAsync { get; }

    public Func<IServiceProvider, IRequest, CancellationToken, Task<OperationResult>> InvokeAsync { get; }

    public Action<IServiceProvider> ResolveHandler { get; }

    public Func<IServiceProvider, IReadOnlyList<IRequest>, IValidationContext, CancellationToken, Task>? ValidateBatchAsync { get; }

    public Func<IServiceProvider, IReadOnlyList<IRequest>, CancellationToken, Task<OperationResult>>? InvokeBatchAsync { get; }
}
