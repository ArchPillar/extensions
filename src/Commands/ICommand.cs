namespace ArchPillar.Extensions.Commands;

/// <summary>
/// A command that mutates state and produces no payload — only a status. The
/// dispatcher still returns an <see cref="ArchPillar.Extensions.Operations.OperationResult"/>
/// so callers can observe the outcome.
/// </summary>
/// <remarks>
/// A command type is registered against exactly one
/// <see cref="ICommandHandler{TCommand}"/>. Cross-cutting concerns are layered
/// as middlewares on the shared command pipeline.
/// </remarks>
public interface ICommand : IRequest
{
}

/// <summary>
/// A command that mutates state and produces a payload of type
/// <typeparamref name="TResult"/> on success. The dispatcher returns an
/// <see cref="ArchPillar.Extensions.Operations.OperationResult{TValue}"/>.
/// </summary>
/// <typeparam name="TResult">The payload type on success.</typeparam>
/// <remarks>
/// A command type is registered against exactly one
/// <see cref="ICommandHandler{TCommand, TResult}"/>.
/// </remarks>
public interface ICommand<out TResult> : IRequest
{
}
