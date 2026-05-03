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
    protected static OperationResult<TResult> Ok(TResult value) => OperationResult.Ok(value);

    /// <summary>Returns a successful <see cref="OperationStatus.Created"/> result carrying <paramref name="value"/>.</summary>
    protected static OperationResult<TResult> Created(TResult value) => OperationResult.Created(value);

    /// <summary>Returns a successful <see cref="OperationStatus.Accepted"/> result carrying <paramref name="value"/>.</summary>
    protected static OperationResult<TResult> Accepted(TResult value) => OperationResult.Accepted(value);

    /// <inheritdoc cref="OperationResult.NotFound(string, string?, IReadOnlyDictionary{string, object?}?, string?)"/>
    protected static OperationFailure NotFound(string detail, string? type = null) => OperationResult.NotFound(detail, type);

    /// <inheritdoc cref="OperationResult.Conflict(string, string?, IReadOnlyDictionary{string, IReadOnlyList{OperationError}}?, IReadOnlyDictionary{string, object?}?, string?)"/>
    protected static OperationFailure Conflict(string detail, string? type = null) => OperationResult.Conflict(detail, type);

    /// <inheritdoc cref="OperationResult.Unauthorized(string, string?, IReadOnlyDictionary{string, object?}?, string?)"/>
    protected static OperationFailure Unauthorized(string detail, string? type = null) => OperationResult.Unauthorized(detail, type);

    /// <inheritdoc cref="OperationResult.Forbidden(string, string?, IReadOnlyDictionary{string, object?}?, string?)"/>
    protected static OperationFailure Forbidden(string detail, string? type = null) => OperationResult.Forbidden(detail, type);

    /// <inheritdoc cref="OperationResult.BadRequest(string, string?, IReadOnlyDictionary{string, IReadOnlyList{OperationError}}?, IReadOnlyDictionary{string, object?}?, string?)"/>
    protected static OperationFailure BadRequest(string detail, string? type = null) => OperationResult.BadRequest(detail, type);

    /// <inheritdoc cref="CommandHandlerBase{TCommand}.EnsureFound{T}"/>
    protected static void EnsureFound<T>([NotNull] T? entity, string? detail = null)
        where T : class
    {
        if (entity is null)
        {
            throw new OperationException(OperationStatus.NotFound, detail ?? $"{typeof(T).Name} not found.");
        }
    }

    /// <inheritdoc cref="CommandHandlerBase{TCommand}.Ensure"/>
    protected static void Ensure(bool condition, OperationStatus status, string detail)
    {
        if (!condition)
        {
            throw new OperationException(status, detail);
        }
    }

    /// <inheritdoc cref="CommandHandlerBase{TCommand}.EnsureAuthorized"/>
    protected static void EnsureAuthorized(bool condition, string detail)
        => Ensure(condition, OperationStatus.Forbidden, detail);

    /// <inheritdoc cref="CommandHandlerBase{TCommand}.EnsureNoConflict"/>
    protected static void EnsureNoConflict(bool condition, string detail)
        => Ensure(condition, OperationStatus.Conflict, detail);
}
