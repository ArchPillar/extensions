using ArchPillar.Extensions.Commands;
using ArchPillar.Extensions.Operations;

namespace Command.WebApiSample.Notes.Commands;

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
