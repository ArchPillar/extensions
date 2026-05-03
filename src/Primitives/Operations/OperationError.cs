using System.Text.Json.Serialization;

namespace ArchPillar.Extensions.Operations;

/// <summary>
/// A single error item carried inside an <see cref="OperationProblem"/>.
/// Modelled after RFC 7807 problem-detail members so the JSON shape is
/// directly compatible with HTTP problem+json consumers.
/// </summary>
/// <param name="Type">A short identifier for the error class (e.g. <c>"required"</c>, <c>"out_of_range"</c>). RFC 7807 calls this <c>type</c>.</param>
/// <param name="Detail">A human-readable explanation of this specific occurrence.</param>
/// <param name="Status">The HTTP-aligned status the validator that produced this error wants the operation to surface as.</param>
/// <param name="Extensions">Free-form additional members, JSON-serializable. Convention is to populate well-known keys (<c>min</c>, <c>max</c>, <c>actual</c>, <c>pattern</c>, <c>length</c>, <c>allowed</c>) where they apply.</param>
public sealed record OperationError(
    [property: JsonPropertyName("type")]       string Type,
    [property: JsonPropertyName("detail")]     string Detail,
    [property: JsonPropertyName("status")]     OperationStatus Status,
    [property: JsonPropertyName("extensions")] IReadOnlyDictionary<string, object?>? Extensions = null);
