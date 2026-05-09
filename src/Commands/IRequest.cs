namespace ArchPillar.Extensions.Commands;

/// <summary>
/// Base marker interface for everything the dispatcher routes. Plumbing
/// detail — application code targets <see cref="ICommand"/> or
/// <see cref="ICommand{TResult}"/> instead.
/// </summary>
/// <remarks>
/// This marker is what allows the router to expose a single, untyped pipeline
/// (<c>Pipeline&lt;CommandContext&gt;</c>) and still recover the original
/// command type when routing.
/// </remarks>
public interface IRequest
{
}
