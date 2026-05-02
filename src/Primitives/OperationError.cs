namespace ArchPillar.Extensions.Primitives;

/// <summary>
/// A single error attached to an <see cref="OperationResult"/>.
/// </summary>
/// <param name="Code">A short, machine-readable identifier (e.g. <c>"required"</c>, <c>"out_of_range"</c>).</param>
/// <param name="Message">A human-readable description of the error.</param>
/// <param name="Field">
/// The name of the input field the error applies to, when relevant. <c>null</c> for
/// errors that are not tied to a single field.
/// </param>
/// <param name="Details">
/// Optional structured detail attached to the error (e.g. allowed values,
/// minimum/maximum, conflicting identifiers). <c>null</c> when not used.
/// </param>
public sealed record OperationError(
    string Code,
    string Message,
    string? Field = null,
    IReadOnlyDictionary<string, object?>? Details = null);
