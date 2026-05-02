using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Primitives;

namespace ArchPillar.Extensions.Commands.Internal;

/// <summary>
/// Captures the typed call site for a command at registration time. Each
/// <c>AddCommandHandler&lt;TCommand, THandler&gt;()</c> call adds one of these
/// to the service collection. The router resolves descriptors lazily on first
/// dispatch of a given command type.
/// </summary>
internal sealed class CommandInvokerDescriptor
{
    public CommandInvokerDescriptor(
        Type commandType,
        Func<IServiceProvider, IRequest, IValidationContext, CancellationToken, Task> validateAsync,
        Func<IServiceProvider, IRequest, CancellationToken, Task<OperationResult>> invokeAsync,
        Func<IServiceProvider, IReadOnlyList<IRequest>, CancellationToken, Task<IReadOnlyList<OperationResult>>>? invokeBatchAsync = null)
    {
        CommandType = commandType;
        ValidateAsync = validateAsync;
        InvokeAsync = invokeAsync;
        InvokeBatchAsync = invokeBatchAsync;
    }

    public Type CommandType { get; }

    public Func<IServiceProvider, IRequest, IValidationContext, CancellationToken, Task> ValidateAsync { get; }

    public Func<IServiceProvider, IRequest, CancellationToken, Task<OperationResult>> InvokeAsync { get; }

    public Func<IServiceProvider, IReadOnlyList<IRequest>, CancellationToken, Task<IReadOnlyList<OperationResult>>>? InvokeBatchAsync { get; }
}
