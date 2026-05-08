namespace Command.WebApiSample.Notes.Requests;

internal sealed record CreateNoteRequest(string? Title, string? Body);
