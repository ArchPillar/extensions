using System.Diagnostics.CodeAnalysis;
using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Primitives;

namespace ArchPillar.Extensions.Commands;

/// <summary>
/// Optional base class for command handlers that produce a payload of type
/// <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <typeparam name="TResult">The payload type returned on success.</typeparam>
public abstract class CommandHandlerBase<TCommand, TResult> : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    /// <inheritdoc/>
    public abstract Task ValidateAsync(TCommand command, IValidationContext context, CancellationToken cancellationToken);

    /// <inheritdoc/>
    public abstract Task<OperationResult<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken);

    /// <summary>Returns a successful <see cref="OperationStatus.Ok"/> result carrying <paramref name="value"/>.</summary>
    protected static OperationResult<TResult> Ok(TResult value) => OperationResult<TResult>.Ok(value);

    /// <summary>Returns a successful <see cref="OperationStatus.Created"/> result carrying <paramref name="value"/>.</summary>
    protected static OperationResult<TResult> Created(TResult value) => OperationResult<TResult>.Created(value);

    /// <summary>Returns a typed <see cref="OperationStatus.NotFound"/> result.</summary>
    protected static OperationResult<TResult> NotFound(string? message = null) => OperationResult<TResult>.NotFound(message);

    /// <summary>Returns a typed <see cref="OperationStatus.Conflict"/> result.</summary>
    protected static OperationResult<TResult> Conflict(string? message = null) => OperationResult<TResult>.Conflict(message);

    /// <summary>Returns a typed <see cref="OperationStatus.BadRequest"/> result.</summary>
    protected static OperationResult<TResult> BadRequest(string? detail = null) => OperationResult<TResult>.BadRequest(detail);

    /// <inheritdoc cref="CommandHandlerBase{TCommand}.EnsureFound{T}"/>
    protected static void EnsureFound<T>([NotNull] T? entity, string? message = null)
        where T : class
    {
        if (entity is null)
        {
            throw new OperationException(OperationStatus.NotFound, message ?? $"{typeof(T).Name} not found.");
        }
    }

    /// <inheritdoc cref="CommandHandlerBase{TCommand}.Ensure"/>
    protected static void Ensure(bool condition, OperationStatus status, string? message = null)
    {
        if (!condition)
        {
            throw new OperationException(status, message);
        }
    }

    /// <inheritdoc cref="CommandHandlerBase{TCommand}.EnsureAuthorized"/>
    protected static void EnsureAuthorized(bool condition, string? message = null)
        => Ensure(condition, OperationStatus.Forbidden, message);

    /// <inheritdoc cref="CommandHandlerBase{TCommand}.EnsureNoConflict"/>
    protected static void EnsureNoConflict(bool condition, string? message = null)
        => Ensure(condition, OperationStatus.Conflict, message);
}
