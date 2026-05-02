namespace ArchPillar.Extensions.Commands;

/// <summary>
/// Base marker interface for handler types. Plumbing detail — application code
/// targets <see cref="ICommandHandler{TCommand}"/>,
/// <see cref="ICommandHandler{TCommand, TResult}"/>, or
/// <see cref="IBatchCommandHandler{TCommand}"/> /
/// <see cref="IBatchCommandHandler{TCommand, TResult}"/> instead.
/// </summary>
public interface IRequestHandler
{
}
