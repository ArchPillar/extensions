using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Operations;

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

    /// <summary>
    /// <c>true</c> when the command implements <c>ICommand&lt;TResult&gt;</c>.
    /// The router uses this to decide whether to populate
    /// <see cref="CommandContext.BatchResults"/> on the iterate-batch
    /// success path — non-result batches don't need it, since the dispatcher
    /// returns a plain <see cref="OperationResult"/> regardless.
    /// </summary>
    public bool ProducesResult { get; }

    public Func<IServiceProvider, IRequest, IValidationContext, CancellationToken, Task> ValidateAsync { get; }

    public Func<IServiceProvider, IRequest, CancellationToken, Task<OperationResult>> InvokeAsync { get; }

    /// <summary>
    /// Resolves the registered handler instance from the supplied service
    /// provider so callers can verify constructor dependencies are satisfied.
    /// Used by <c>ValidateCommandRegistrations()</c>.
    /// </summary>
    public Action<IServiceProvider> ResolveHandler { get; }

    public Func<IServiceProvider, IReadOnlyList<IRequest>, IValidationContext, CancellationToken, Task>? ValidateBatchAsync { get; }

    public Func<IServiceProvider, IReadOnlyList<IRequest>, CancellationToken, Task<OperationResult>>? InvokeBatchAsync { get; }
}
