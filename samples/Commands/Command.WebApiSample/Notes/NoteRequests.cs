namespace Command.WebApiSample.Notes;

/// <summary>
/// Wire-shape DTOs for the Notes endpoints. Kept separate from the command
/// records so the public HTTP contract can evolve independently of the
/// internal command surface — and so the same commands can be dispatched
/// from non-HTTP callers (background jobs, internal services) without those
/// callers depending on the API layer.
/// </summary>
internal static class NoteRequests
{
    public sealed record CreateNoteRequest(string? Title, string? Body);

    public sealed record UpdateNoteRequest(string? Title, string? Body);
}
