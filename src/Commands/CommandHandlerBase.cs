using System.Diagnostics.CodeAnalysis;
using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Primitives;

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

    /// <summary>Returns <see cref="OperationStatus.Ok"/>.</summary>
    protected static OperationResult Ok() => OperationResult.Ok();

    /// <summary>Returns <see cref="OperationStatus.Created"/>.</summary>
    protected static OperationResult Created() => OperationResult.Created();

    /// <summary>Returns <see cref="OperationStatus.Accepted"/>.</summary>
    protected static OperationResult Accepted() => OperationResult.Accepted();

    /// <summary>Returns <see cref="OperationStatus.NoContent"/>.</summary>
    protected static OperationResult NoContent() => OperationResult.NoContent();

    /// <summary>Returns <see cref="OperationStatus.NotFound"/>.</summary>
    protected static OperationResult NotFound(string? message = null) => OperationResult.NotFound(message);

    /// <summary>Returns <see cref="OperationStatus.Conflict"/>.</summary>
    protected static OperationResult Conflict(string? message = null) => OperationResult.Conflict(message);

    /// <summary>Returns <see cref="OperationStatus.Unauthorized"/>.</summary>
    protected static OperationResult Unauthorized(string? message = null) => OperationResult.Unauthorized(message);

    /// <summary>Returns <see cref="OperationStatus.Forbidden"/>.</summary>
    protected static OperationResult Forbidden(string? message = null) => OperationResult.Forbidden(message);

    /// <summary>Returns <see cref="OperationStatus.BadRequest"/>.</summary>
    protected static OperationResult BadRequest(params OperationError[] errors) => OperationResult.BadRequest(errors);

    /// <summary>Returns <see cref="OperationStatus.BadRequest"/> with a single error message.</summary>
    protected static OperationResult BadRequest(string message) => OperationResult.BadRequest(message);

    /// <summary>Throws an <see cref="OperationException"/> with status <see cref="OperationStatus.NotFound"/> if <paramref name="entity"/> is <c>null</c>.</summary>
    protected static void EnsureFound<T>([NotNull] T? entity, string? message = null)
        where T : class
    {
        if (entity is null)
        {
            throw new OperationException(OperationStatus.NotFound, message ?? $"{typeof(T).Name} not found.");
        }
    }

    /// <summary>Throws if <paramref name="condition"/> is <c>false</c>.</summary>
    protected static void Ensure(bool condition, OperationStatus status, string? message = null)
    {
        if (!condition)
        {
            throw new OperationException(status, message);
        }
    }

    /// <summary>Throws <see cref="OperationStatus.Forbidden"/> if <paramref name="condition"/> is <c>false</c>.</summary>
    protected static void EnsureAuthorized(bool condition, string? message = null)
        => Ensure(condition, OperationStatus.Forbidden, message);

    /// <summary>Throws <see cref="OperationStatus.Conflict"/> if <paramref name="condition"/> is <c>false</c>.</summary>
    protected static void EnsureNoConflict(bool condition, string? message = null)
        => Ensure(condition, OperationStatus.Conflict, message);
}
