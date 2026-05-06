using ArchPillar.Extensions.Commands;
using ArchPillar.Extensions.Operations;
using Command.WebApiSample.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Command.WebApiSample.Notes;

internal static class NotesEndpoints
{
    public static IEndpointRouteBuilder MapNotes(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/notes").WithTags("Notes");

        // Reads bypass the dispatcher — they don't mutate state and don't need
        // the validation/transaction shell that wraps commands. EF Core
        // projections (or ArchPillar.Extensions.Mapper) belong here.
        group.MapGet("/", async (NotesDbContext context, CancellationToken cancellationToken) =>
        {
            var notes = await context.Notes
                .OrderByDescending(note => note.CreatedAt)
                .Select(note => new NoteResponse(note.Id, note.Title, note.Body, note.IsArchived, note.CreatedAt, note.UpdatedAt))
                .ToListAsync(cancellationToken);
            return Results.Ok(notes);
        });

        group.MapGet("/{id:guid}", async (Guid id, NotesDbContext context, CancellationToken cancellationToken) =>
        {
            var note = await context.Notes
                .Where(candidate => candidate.Id == id)
                .Select(candidate => new NoteResponse(candidate.Id, candidate.Title, candidate.Body, candidate.IsArchived, candidate.CreatedAt, candidate.UpdatedAt))
                .FirstOrDefaultAsync(cancellationToken);
            return note is null ? Results.NotFound() : Results.Ok(note);
        });

        // Writes accept a wire-shape DTO from the body, then map it into the
        // internal command. The command type is never part of the public API
        // contract, so the two can evolve independently and the same command
        // is reachable from non-HTTP callers without the DTO tagging along.
        group.MapPost("/", async (
            NoteRequests.CreateNoteRequest request,
            ICommandDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(request);

            var command = new CreateNote(request.Title ?? string.Empty, request.Body ?? string.Empty);
            OperationResult<Guid> result = await dispatcher.SendAsync(command, cancellationToken);
            return result.IsSuccess
                ? Results.Created($"/notes/{result.Value}", new { id = result.Value })
                : result.ToProblemResult();
        });

        group.MapPut("/{id:guid}", async (
            Guid id,
            NoteRequests.UpdateNoteRequest request,
            ICommandDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(request);

            var command = new UpdateNote(id, request.Title ?? string.Empty, request.Body ?? string.Empty);
            OperationResult result = await dispatcher.SendAsync(command, cancellationToken);
            return result.IsSuccess ? Results.NoContent() : result.ToProblemResult();
        });

        group.MapPost("/{id:guid}/archive", async (
            Guid id,
            ICommandDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            OperationResult result = await dispatcher.SendAsync(new ArchiveNote(id), cancellationToken);
            return result.IsSuccess ? Results.NoContent() : result.ToProblemResult();
        });

        // Batch — one round-trip, per-item results stitched back in input order.
        group.MapPost("/batch", async (
            NoteRequests.CreateNoteRequest[] requests,
            ICommandDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(requests);

            var commands = new CreateNote[requests.Length];
            for (var i = 0; i < requests.Length; i++)
            {
                commands[i] = new CreateNote(requests[i].Title ?? string.Empty, requests[i].Body ?? string.Empty);
            }

            IReadOnlyList<OperationResult<Guid>> results =
                await dispatcher.SendBatchAsync<CreateNote, Guid>(commands, cancellationToken);
            return Results.Ok(results.Select(result => new BatchItemResponse(
                (int)result.Status,
                result.Value,
                result.Problem?.Detail)));
        });

        return routes;
    }

    internal sealed record NoteResponse(
        Guid Id,
        string Title,
        string Body,
        bool IsArchived,
        DateTime CreatedAt,
        DateTime? UpdatedAt);

    internal sealed record BatchItemResponse(int Status, Guid Id, string? Detail);
}
