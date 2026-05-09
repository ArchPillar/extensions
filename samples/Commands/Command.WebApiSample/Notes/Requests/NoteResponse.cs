namespace Command.WebApiSample.Notes.Requests;

internal sealed record NoteResponse(
    Guid Id,
    string Title,
    string Body,
    bool IsArchived,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
