using ArchPillar.Extensions.Commands;
using ArchPillar.Extensions.Commands.Validation;
using ArchPillar.Extensions.Operations;

namespace Command.WebApiSample.Notes.Commands;

internal sealed class CreateNoteBatchHandler(NotesDbContext context, TimeProvider clock)
    : IBatchCommandHandler<CreateNote, Guid>
{
    public Task ValidateAsync(
        IReadOnlyList<CreateNote> commands,
        IValidationContext validation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(validation);

        for (var i = 0; i < commands.Count; i++)
        {
            CreateNote command = commands[i];
            validation
                .NotBlank(command.Title, field: $"commands[{i}].Title")
                .MaxLength(command.Title, 120, field: $"commands[{i}].Title")
                .NotBlank(command.Body, field: $"commands[{i}].Body")
                .MaxLength(command.Body, 4_000, field: $"commands[{i}].Body");
        }

        return Task.CompletedTask;
    }

    public async Task<OperationResult<IReadOnlyList<Guid>>> HandleBatchAsync(
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

        var ids = new Guid[notes.Length];
        for (var i = 0; i < notes.Length; i++)
        {
            ids[i] = notes[i].Id;
        }

        return OperationResult.Ok<IReadOnlyList<Guid>>(ids);
    }
}
