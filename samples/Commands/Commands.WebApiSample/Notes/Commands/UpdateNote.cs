using ArchPillar.Extensions.Commands;

namespace Commands.WebApiSample.Notes.Commands;

internal sealed record UpdateNote(Guid Id, string Title, string Body) : ICommand;
