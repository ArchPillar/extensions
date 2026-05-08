using ArchPillar.Extensions.Commands;

namespace Command.WebApiSample.Notes.Commands;

internal sealed record ArchiveNote(Guid Id) : ICommand;
