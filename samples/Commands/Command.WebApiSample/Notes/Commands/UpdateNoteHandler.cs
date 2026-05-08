using ArchPillar.Extensions.Commands;
using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Operations;
using Microsoft.EntityFrameworkCore;

namespace Command.WebApiSample.Notes.Commands;

internal sealed class UpdateNoteHandler(NotesDbContext context, TimeProvider clock)
    : CommandHandlerBase<UpdateNote>
{
    public override async Task ValidateAsync(UpdateNote command, IValidationContext validation, CancellationToken cancellationToken)
    {
        validation
            .NotBlank(command.Title)
            .MaxLength(command.Title, 120)
            .NotBlank(command.Body)
            .MaxLength(command.Body, 4_000);

        var note = await context.Notes.FindAsync([command.Id], cancellationToken);
        validation.Exists(note);
        validation.Conflict(note is null || !note.IsArchived, "Cannot update an archived note.");
    }

    public override async Task<OperationResult> HandleAsync(UpdateNote command, CancellationToken cancellationToken)
    {
        var note = await context.Notes.FindAsync([command.Id], cancellationToken);
        EnsureFound(note);

        note.Title = command.Title;
        note.Body = command.Body;
        note.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await context.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
