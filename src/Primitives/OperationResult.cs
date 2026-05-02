namespace ArchPillar.Extensions.Primitives;

/// <summary>
/// The outcome of an operation. Carries an HTTP-aligned <see cref="Status"/>,
/// optional <see cref="Errors"/>, and an optional <see cref="Exception"/>.
/// </summary>
/// <remarks>
/// <para>
/// The base <see cref="OperationResult"/> represents an outcome with no value.
/// Use <see cref="OperationResult{TValue}"/> when an outcome carries a payload.
/// </para>
/// <para>
/// Implicit conversions are provided to reduce boilerplate at common call sites:
/// <list type="bullet">
/// <item><description><see cref="op_Implicit(OperationResult)"/> to <see cref="Task{TResult}"/> wraps the result in a completed task — useful for synchronous handlers that return <c>Task&lt;OperationResult&gt;</c> without <see cref="Task.FromResult{TResult}(TResult)"/>.</description></item>
/// <item><description><see cref="op_Implicit(OperationResult)"/> to <see cref="System.Exception"/> produces an <see cref="OperationException"/> that carries this result, enabling <c>throw result;</c> from any code path.</description></item>
/// </list>
/// </para>
/// </remarks>
public class OperationResult
{
    /// <summary>
    /// Initializes a new <see cref="OperationResult"/>. Prefer the static
    /// factories (<see cref="Ok(OperationStatus)"/>, <see cref="NotFound(string?)"/>,
    /// etc.) at call sites.
    /// </summary>
    public OperationResult()
    {
    }

    /// <summary>
    /// The HTTP-aligned status of the operation.
    /// </summary>
    public OperationStatus Status { get; init; } = OperationStatus.None;

    /// <summary>
    /// The errors attached to the operation. Empty for successful results;
    /// typically populated for validation and bad-request outcomes.
    /// </summary>
    public IReadOnlyList<OperationError> Errors { get; init; } = [];

    /// <summary>
    /// The exception that caused the operation to fail, if any. Set by the
    /// dispatcher when an unhandled exception escapes a handler. May also be
    /// set by handlers that want to preserve the original cause alongside a
    /// translated <see cref="Status"/>.
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// <c>true</c> when <see cref="Status"/> is in the 2xx range.
    /// </summary>
    public bool IsSuccess => (int)Status is >= 200 and < 300;

    /// <summary>
    /// <c>true</c> when <see cref="IsSuccess"/> is <c>false</c>.
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Wraps this result in a completed <see cref="Task{TResult}"/>. Allows
    /// non-async methods to return an <see cref="OperationResult"/> directly:
    /// <code>
    /// public Task&lt;OperationResult&gt; HandleAsync(...) => Ok();
    /// </code>
    /// </summary>
    /// <param name="result">The result to wrap.</param>
    public static implicit operator Task<OperationResult>(OperationResult result)
        => Task.FromResult(result);

    /// <summary>
    /// Produces an <see cref="OperationException"/> that carries this result.
    /// Allows <c>throw result;</c> from any code path:
    /// <code>
    /// if (entity is null) throw NotFound("entity missing");
    /// </code>
    /// </summary>
    /// <param name="result">The result to wrap.</param>
    public static implicit operator Exception(OperationResult result)
        => new OperationException(result);

    /// <summary>
    /// Throws an <see cref="OperationException"/> if the result is a failure.
    /// </summary>
    /// <returns>The current result, for chaining.</returns>
    public OperationResult ThrowIfFailed()
    {
        if (IsFailure)
        {
            throw new OperationException(this);
        }

        return this;
    }

    /// <summary>Creates a successful result with status <see cref="OperationStatus.Ok"/> by default.</summary>
    public static OperationResult Ok(OperationStatus status = OperationStatus.Ok)
        => new() { Status = status };

    /// <summary>Creates a successful result with status <see cref="OperationStatus.Created"/>.</summary>
    public static OperationResult Created()
        => new() { Status = OperationStatus.Created };

    /// <summary>Creates a successful result with status <see cref="OperationStatus.Accepted"/>.</summary>
    public static OperationResult Accepted()
        => new() { Status = OperationStatus.Accepted };

    /// <summary>Creates a successful result with status <see cref="OperationStatus.NoContent"/>.</summary>
    public static OperationResult NoContent()
        => new() { Status = OperationStatus.NoContent };

    /// <summary>Creates a <see cref="OperationStatus.BadRequest"/> result.</summary>
    public static OperationResult BadRequest(params OperationError[] errors)
        => new() { Status = OperationStatus.BadRequest, Errors = errors };

    /// <summary>Creates a <see cref="OperationStatus.BadRequest"/> result from a message.</summary>
    public static OperationResult BadRequest(string message)
        => new() { Status = OperationStatus.BadRequest, Errors = [new OperationError("bad_request", message)] };

    /// <summary>Creates a <see cref="OperationStatus.Unauthorized"/> result.</summary>
    public static OperationResult Unauthorized(string? message = null)
        => new()
        {
            Status = OperationStatus.Unauthorized,
            Errors = message is null ? [] : [new OperationError("unauthorized", message)],
        };

    /// <summary>Creates a <see cref="OperationStatus.Forbidden"/> result.</summary>
    public static OperationResult Forbidden(string? message = null)
        => new()
        {
            Status = OperationStatus.Forbidden,
            Errors = message is null ? [] : [new OperationError("forbidden", message)],
        };

    /// <summary>Creates a <see cref="OperationStatus.NotFound"/> result.</summary>
    public static OperationResult NotFound(string? message = null)
        => new()
        {
            Status = OperationStatus.NotFound,
            Errors = message is null ? [] : [new OperationError("not_found", message)],
        };

    /// <summary>Creates a <see cref="OperationStatus.Conflict"/> result.</summary>
    public static OperationResult Conflict(string? message = null)
        => new()
        {
            Status = OperationStatus.Conflict,
            Errors = message is null ? [] : [new OperationError("conflict", message)],
        };

    /// <summary>Creates a <see cref="OperationStatus.UnprocessableEntity"/> result with the supplied validation errors.</summary>
    public static OperationResult ValidationFailed(IReadOnlyList<OperationError> errors)
        => new() { Status = OperationStatus.UnprocessableEntity, Errors = errors };

    /// <summary>Creates a failure result from an <see cref="System.Exception"/>.</summary>
    public static OperationResult Failed(Exception exception, OperationStatus status = OperationStatus.InternalServerError)
        => new() { Status = status, Exception = exception };

    /// <summary>Creates a failure result with the supplied status and message.</summary>
    public static OperationResult Failed(OperationStatus status, string message)
        => new() { Status = status, Errors = [new OperationError("error", message)] };
}
