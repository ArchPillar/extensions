using ArchPillar.Extensions.Operations;

namespace Primitives.WebApiSample.Infrastructure;

/// <summary>
/// Maps <see cref="OperationResult"/> failures onto Minimal-API
/// <see cref="IResult"/> values. The status comes straight off
/// <see cref="OperationResult.Status"/> and the body is the
/// <see cref="OperationProblem"/> emitted as <c>application/problem+json</c>,
/// so an RFC 7807 consumer reads the failure without any translation.
/// </summary>
/// <remarks>
/// Success is intentionally not handled here — the endpoint knows the right
/// success shape (<c>Created</c> with a Location, <c>Ok</c> with a projection,
/// <c>NoContent</c>) and produces it explicitly.
/// </remarks>
internal static class OperationResultExtensions
{
    public static IResult ToProblemResult(this OperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return Results.Json(
            result.Problem,
            statusCode: (int)result.Status,
            contentType: "application/problem+json");
    }

    /// <summary>
    /// Resolves a typed result: on failure emits the problem response, on
    /// success hands the payload to <paramref name="onSuccess"/> so the
    /// endpoint can pick the 200/201 shape and Location header.
    /// </summary>
    public static IResult ToResult<TValue>(
        this OperationResult<TValue> result,
        Func<TValue, IResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);

        return result.IsSuccess
            ? onSuccess(result.Value!)
            : result.ToProblemResult();
    }
}
