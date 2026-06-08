namespace Commands.WebApiSample.Notes.Requests;

internal sealed record UpdateNoteRequest(string? Title, string? Body);
