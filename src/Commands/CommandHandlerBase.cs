using System.Diagnostics.CodeAnalysis;
using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Operations;

namespace ArchPillar.Extensions.Commands;

/// <summary>
/// Optional base class for no-result command handlers. Provides
/// status-factory and assert helpers in the spirit of ASP.NET Core's
/// <c>ControllerBase</c>. Forces the subclass to make a deliberate choice
/// about validation by leaving <see cref="ValidateAsync"/> abstract.
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
public abstract class CommandHandlerBase<TCommand> : ICommandHandler<TCommand>
    where TCommand : ICommand
{
    /// <inheritdoc/>
    public abstract Task ValidateAsync(TCommand command, IValidationContext context, CancellationToken cancellationToken);

    /// <inheritdoc/>
    public abstract Task<OperationResult> HandleAsync(TCommand command, CancellationToken cancellationToken);

    /// <summary>Returns a successful <see cref="OperationStatus.Ok"/> result.</summary>
    protected static OperationResult Ok() => OperationResult.Ok();

    /// <summary>Returns a successful <see cref="OperationStatus.Created"/> result.</summary>
    protected static OperationResult Created() => OperationResult.Created();

    /// <summary>Returns a successful <see cref="OperationStatus.Accepted"/> result.</summary>
    protected static OperationResult Accepted() => OperationResult.Accepted();

    /// <summary>Returns a successful <see cref="OperationStatus.NoContent"/> result.</summary>
    protected static OperationResult NoContent() => OperationResult.NoContent();

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

    /// <summary>Throws an <see cref="OperationException"/> with status <see cref="OperationStatus.NotFound"/> if <paramref name="entity"/> is <c>null</c>.</summary>
    protected static void EnsureFound<T>([NotNull] T? entity, string? detail = null)
        where T : class
    {
        if (entity is null)
        {
            throw new OperationException(OperationStatus.NotFound, detail ?? $"{typeof(T).Name} not found.");
        }
    }

    /// <summary>Throws if <paramref name="condition"/> is <c>false</c>.</summary>
    protected static void Ensure(bool condition, OperationStatus status, string detail)
    {
        if (!condition)
        {
            throw new OperationException(status, detail);
        }
    }

    /// <summary>Throws <see cref="OperationStatus.Forbidden"/> if <paramref name="condition"/> is <c>false</c>.</summary>
    protected static void EnsureAuthorized(bool condition, string detail)
        => Ensure(condition, OperationStatus.Forbidden, detail);

    /// <summary>Throws <see cref="OperationStatus.Conflict"/> if <paramref name="condition"/> is <c>false</c>.</summary>
    protected static void EnsureNoConflict(bool condition, string detail)
        => Ensure(condition, OperationStatus.Conflict, detail);
}
