namespace Commands.WebApiSample.Notes.Requests;

internal sealed record CreateNoteRequest(string? Title, string? Body);
