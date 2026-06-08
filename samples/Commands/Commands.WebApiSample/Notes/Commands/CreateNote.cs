using ArchPillar.Extensions.Commands;

namespace Commands.WebApiSample.Notes.Commands;

internal sealed record CreateNote(string Title, string Body) : ICommand<Guid>;
