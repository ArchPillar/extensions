using ArchPillar.Extensions.Commands;
using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Operations;
using Microsoft.EntityFrameworkCore;

namespace Command.WebApiSample.Notes;

internal sealed record CreateNote(string Title, string Body) : ICommand<Guid>;

internal sealed record UpdateNote(Guid Id, string Title, string Body) : ICommand;

internal sealed record ArchiveNote(Guid Id) : ICommand;

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

internal sealed class CreateNoteBatchHandler(NotesDbContext context, TimeProvider clock)
    : IBatchCommandHandler<CreateNote, Guid>
{
    public async Task<IReadOnlyList<OperationResult<Guid>>> HandleBatchAsync(
        IReadOnlyList<CreateNote> commands,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commands);

        var now = clock.GetUtcNow().UtcDateTime;
        var notes = new Note[commands.Count];
        for (var i = 0; i < commands.Count; i++)
        {
            notes[i] = new Note
            {
                Id = Guid.NewGuid(),
                Title = commands[i].Title,
                Body = commands[i].Body,
                CreatedAt = now,
            };
        }

        context.Notes.AddRange(notes);
        await context.SaveChangesAsync(cancellationToken);

        var results = new OperationResult<Guid>[notes.Length];
        for (var i = 0; i < notes.Length; i++)
        {
            results[i] = OperationResult.Created(notes[i].Id);
        }

        return results;
    }
}
