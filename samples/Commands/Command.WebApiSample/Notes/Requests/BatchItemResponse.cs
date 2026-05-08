namespace Command.WebApiSample.Notes.Requests;

internal sealed record BatchItemResponse(int Status, Guid Id, string? Detail);
