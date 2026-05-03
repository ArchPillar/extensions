using System.Text.Json.Serialization;

namespace ArchPillar.Extensions.Primitives;

/// <summary>
/// The error body of a failed <see cref="OperationResult"/>. Modelled after
/// RFC 7807 <c>application/problem+json</c> so the JSON shape can be returned
/// directly from an HTTP boundary.
/// </summary>
/// <remarks>
/// Field-bearing errors (validation against a specific input property) are
/// aggregated into <see cref="Errors"/> keyed by field name. Top-level errors
/// (auth, not-found, conflict) are described by <see cref="Title"/> and
/// <see cref="Detail"/> with optional structured extras in
/// <see cref="Extensions"/>.
/// </remarks>
public sealed class OperationProblem
{
    /// <summary>Short identifier for the problem class. RFC 7807 <c>type</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>Short, generic summary of the problem class. Doesn't change per occurrence.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>Human-readable explanation specific to this occurrence.</summary>
    [JsonPropertyName("detail")]
    public string? Detail { get; init; }

    /// <summary>Optional URI identifying this specific occurrence (e.g. a request id).</summary>
    [JsonPropertyName("instance")]
    public string? Instance { get; init; }

    /// <summary>
    /// Field-keyed validation errors. The key is the field name (typically a
    /// property path captured by <c>[CallerArgumentExpression]</c>), the value
    /// is the list of errors collected against that field.
    /// </summary>
    [JsonPropertyName("errors")]
    public IReadOnlyDictionary<string, IReadOnlyList<OperationError>>? Errors { get; init; }

    /// <summary>
    /// Free-form additional members. RFC 7807 <c>extensions</c>. Used for
    /// structured context that doesn't fit the standard members.
    /// </summary>
    [JsonPropertyName("extensions")]
    public IReadOnlyDictionary<string, object?>? Extensions { get; init; }
}
