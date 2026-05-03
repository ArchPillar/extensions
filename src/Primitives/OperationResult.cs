using System.Text.Json.Serialization;

namespace ArchPillar.Extensions.Primitives;

/// <summary>
/// The outcome of an operation. Carries an HTTP-aligned <see cref="Status"/>
/// and an optional <see cref="Problem"/> body describing why the operation
/// failed (RFC 7807 <c>application/problem+json</c> shape).
/// </summary>
/// <remarks>
/// <para>
/// On success <see cref="Problem"/> is <c>null</c> — the result is just a
/// status code, no body to allocate. <see cref="OperationResult{TValue}"/>
/// adds the typed payload.
/// </para>
/// <para>
/// Implicit conversions:
/// <list type="bullet">
/// <item><description>To <see cref="Task{TResult}"/> wraps the result in a completed task — useful for synchronous handlers that return <c>Task&lt;OperationResult&gt;</c> without <see cref="Task.FromResult{TResult}(TResult)"/>.</description></item>
/// <item><description>To <see cref="System.Exception"/> produces an <see cref="OperationException"/> that carries this result, enabling <c>throw result;</c> from any code path.</description></item>
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

    /// <summary>The HTTP-aligned status of the operation.</summary>
    [JsonPropertyName("status")]
    public OperationStatus Status { get; init; } = OperationStatus.None;

    /// <summary>
    /// The error body, populated on failure and <c>null</c> on success.
    /// Modelled after RFC 7807 <c>problem+json</c>.
    /// </summary>
    [JsonPropertyName("problem")]
    public OperationProblem? Problem { get; init; }

    /// <summary>
    /// The exception that caused the operation to fail, if any. Internal-only
    /// — never serialized over the wire. Set by the dispatcher's exception
    /// middleware when an unhandled exception escapes a handler.
    /// </summary>
    [JsonIgnore]
    public Exception? Exception { get; init; }

    /// <summary><c>true</c> when <see cref="Status"/> is in the 2xx range.</summary>
    [JsonIgnore]
    public bool IsSuccess => (int)Status is >= 200 and < 300;

    /// <summary><c>true</c> when <see cref="IsSuccess"/> is <c>false</c>.</summary>
    [JsonIgnore]
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Wraps this result in a completed <see cref="Task{TResult}"/>.
    /// </summary>
    /// <param name="result">The result to wrap.</param>
    public static implicit operator Task<OperationResult>(OperationResult result)
        => Task.FromResult(result);

    /// <summary>
    /// Produces an <see cref="OperationException"/> that carries this result.
    /// Allows <c>throw result;</c> from any code path.
    /// </summary>
    /// <param name="result">The result to wrap.</param>
    public static implicit operator Exception(OperationResult result)
        => new OperationException(result);

    /// <summary>Throws an <see cref="OperationException"/> if the result is a failure.</summary>
    /// <returns>The current result, for chaining.</returns>
    public OperationResult ThrowIfFailed()
    {
        if (IsFailure)
        {
            throw new OperationException(this);
        }

        return this;
    }

    /// <summary>
    /// Asserts the operation succeeded. Throws <see cref="OperationException"/>
    /// on failure. The sync analogue to a <c>void</c>-returning method call.
    /// </summary>
    public void Unwrap() => ThrowIfFailed();

    /// <summary>Creates a successful result with status <see cref="OperationStatus.Ok"/> by default.</summary>
    public static OperationResult Ok(OperationStatus status = OperationStatus.Ok)
        => new() { Status = status };

    /// <summary>Creates a successful <see cref="OperationStatus.Created"/> result.</summary>
    public static OperationResult Created()
        => new() { Status = OperationStatus.Created };

    /// <summary>Creates a successful <see cref="OperationStatus.Accepted"/> result.</summary>
    public static OperationResult Accepted()
        => new() { Status = OperationStatus.Accepted };

    /// <summary>Creates a successful <see cref="OperationStatus.NoContent"/> result.</summary>
    public static OperationResult NoContent()
        => new() { Status = OperationStatus.NoContent };

    /// <summary>Creates a <see cref="OperationStatus.BadRequest"/> failure.</summary>
    public static OperationResult BadRequest(string? detail = null)
        => Failure(OperationStatus.BadRequest, "bad_request", "Bad Request", detail);

    /// <summary>Creates an <see cref="OperationStatus.Unauthorized"/> failure.</summary>
    public static OperationResult Unauthorized(string? detail = null)
        => Failure(OperationStatus.Unauthorized, "unauthorized", "Unauthorized", detail);

    /// <summary>Creates a <see cref="OperationStatus.Forbidden"/> failure.</summary>
    public static OperationResult Forbidden(string? detail = null)
        => Failure(OperationStatus.Forbidden, "forbidden", "Forbidden", detail);

    /// <summary>Creates a <see cref="OperationStatus.NotFound"/> failure.</summary>
    public static OperationResult NotFound(string? detail = null)
        => Failure(OperationStatus.NotFound, "not_found", "Not Found", detail);

    /// <summary>Creates a <see cref="OperationStatus.Conflict"/> failure.</summary>
    public static OperationResult Conflict(string? detail = null)
        => Failure(OperationStatus.Conflict, "conflict", "Conflict", detail);

    /// <summary>Creates a failure result from an <see cref="System.Exception"/>.</summary>
    public static OperationResult Failed(Exception exception, OperationStatus status = OperationStatus.InternalServerError)
        => new()
        {
            Status = status,
            Exception = exception,
            Problem = new OperationProblem
            {
                Type = "internal_error",
                Title = StatusTitle(status),
                Detail = exception?.Message,
            },
        };

    /// <summary>Creates a failure result with the supplied status, title, and detail.</summary>
    public static OperationResult Failure(OperationStatus status, string type, string title, string? detail = null)
        => new()
        {
            Status = status,
            Problem = new OperationProblem
            {
                Type = type,
                Title = title,
                Detail = detail,
            },
        };

    internal static string StatusTitle(OperationStatus status)
        => status switch
        {
            OperationStatus.BadRequest => "Bad Request",
            OperationStatus.Unauthorized => "Unauthorized",
            OperationStatus.Forbidden => "Forbidden",
            OperationStatus.NotFound => "Not Found",
            OperationStatus.Conflict => "Conflict",
            OperationStatus.Gone => "Gone",
            OperationStatus.PreconditionFailed => "Precondition Failed",
            OperationStatus.UnprocessableEntity => "Unprocessable Entity",
            OperationStatus.TooManyRequests => "Too Many Requests",
            OperationStatus.InternalServerError => "Internal Server Error",
            OperationStatus.NotImplemented => "Not Implemented",
            OperationStatus.ServiceUnavailable => "Service Unavailable",
            _ => status.ToString(),
        };
}
