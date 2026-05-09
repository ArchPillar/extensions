namespace Command.WebApiSample.Notes.Requests;

internal sealed record UpdateNoteRequest(string? Title, string? Body);
