using ArchPillar.Extensions.Commands;
using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Operations;

namespace Command.WebApiSample.Notes.Commands;

internal sealed class CreateNoteHandler(NotesDbContext context, TimeProvider clock)
    : CommandHandlerBase<CreateNote, Guid>
{
    public override Task ValidateAsync(CreateNote command, IValidationContext validation, CancellationToken cancellationToken)
    {
        validation
            .NotBlank(command.Title)
            .MaxLength(command.Title, 120)
            .NotBlank(command.Body)
            .MaxLength(command.Body, 4_000);
        return Task.CompletedTask;
    }

    public override async Task<OperationResult<Guid>> HandleAsync(CreateNote command, CancellationToken cancellationToken)
    {
        var note = new Note
        {
            Id = Guid.NewGuid(),
            Title = command.Title,
            Body = command.Body,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        context.Notes.Add(note);
        await context.SaveChangesAsync(cancellationToken);
        return Created(note.Id);
    }
}
