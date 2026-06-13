using ArchPillar.Extensions.Commands;

namespace Commands.WebApiSample.Notes.Commands;

internal sealed record ArchiveNote(Guid Id) : ICommand;
