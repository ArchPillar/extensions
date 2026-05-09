using ArchPillar.Extensions.Operations;

namespace Command.WebApiSample.Infrastructure;

/// <summary>
/// Maps <see cref="OperationResult"/> failures onto Minimal-API <see cref="IResult"/>
/// values. Success cases are intentionally not handled here — the endpoint
/// already knows the right success shape (<c>Created</c>, <c>NoContent</c>,
/// <c>Ok</c>, …) and should produce it explicitly.
/// </summary>
internal static class OperationResultExtensions
{
    public static IResult ToProblemResult(this OperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        // OperationProblem is already shaped like RFC 7807 — the JSON shape
        // round-trips through Minimal-API serialization without translation.
        return Results.Json(result.Problem, statusCode: (int)result.Status);
    }
}
