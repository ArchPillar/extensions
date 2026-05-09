using ArchPillar.Extensions.Commands;
using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Operations;
using Microsoft.EntityFrameworkCore;

namespace Command.WebApiSample.Notes.Commands;

internal sealed class ArchiveNoteHandler(NotesDbContext context, TimeProvider clock)
    : CommandHandlerBase<ArchiveNote>
{
    public override Task ValidateAsync(ArchiveNote command, IValidationContext validation, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public override async Task<OperationResult> HandleAsync(ArchiveNote command, CancellationToken cancellationToken)
    {
        var note = await context.Notes.FindAsync([command.Id], cancellationToken);
        EnsureFound(note);

        if (note.IsArchived)
        {
            return Conflict("Note is already archived.");
        }

        note.IsArchived = true;
        note.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await context.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
