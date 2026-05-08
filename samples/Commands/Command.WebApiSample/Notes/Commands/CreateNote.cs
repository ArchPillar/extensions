using ArchPillar.Extensions.Commands;

namespace Command.WebApiSample.Notes.Commands;

internal sealed record CreateNote(string Title, string Body) : ICommand<Guid>;
