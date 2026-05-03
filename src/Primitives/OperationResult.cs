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
/// status, no body to allocate. <see cref="OperationResult{TValue}"/> adds
/// the typed payload.
/// </para>
/// <para>
/// Construction goes through the static factories on this type. Failure
/// factories return <see cref="OperationFailure"/> so callers can return them
/// from any handler signature — an implicit conversion on
/// <see cref="OperationResult{TValue}"/> picks the failure up without forcing
/// the caller to repeat the value type.
/// </para>
/// </remarks>
public class OperationResult
{
    /// <summary>
    /// Initializes a new <see cref="OperationResult"/>. Prefer the static
    /// factories at call sites.
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
    /// — never serialized over the wire.
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

    // ─────────────────────────────────────────────────────────────────────
    // Success
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Creates a successful result with <see cref="OperationStatus.Ok"/>.</summary>
    public static OperationResult Ok()
        => new() { Status = OperationStatus.Ok };

    /// <summary>
    /// Creates a successful <see cref="OperationResult{TValue}"/> carrying
    /// <paramref name="value"/>. <typeparamref name="TValue"/> is inferred
    /// from the argument.
    /// </summary>
    public static OperationResult<TValue> Ok<TValue>(TValue value)
        => new() { Status = OperationStatus.Ok, Value = value };

    /// <summary>Creates a successful result with <see cref="OperationStatus.Created"/>.</summary>
    public static OperationResult Created()
        => new() { Status = OperationStatus.Created };

    /// <summary>
    /// Creates a successful <see cref="OperationStatus.Created"/> result
    /// carrying <paramref name="value"/>.
    /// </summary>
    public static OperationResult<TValue> Created<TValue>(TValue value)
        => new() { Status = OperationStatus.Created, Value = value };

    /// <summary>Creates a successful result with <see cref="OperationStatus.Accepted"/>.</summary>
    public static OperationResult Accepted()
        => new() { Status = OperationStatus.Accepted };

    /// <summary>
    /// Creates a successful <see cref="OperationStatus.Accepted"/> result
    /// carrying <paramref name="value"/>.
    /// </summary>
    public static OperationResult<TValue> Accepted<TValue>(TValue value)
        => new() { Status = OperationStatus.Accepted, Value = value };

    /// <summary>Creates a successful result with <see cref="OperationStatus.NoContent"/>.</summary>
    public static OperationResult NoContent()
        => new() { Status = OperationStatus.NoContent };

    // ─────────────────────────────────────────────────────────────────────
    // Failure — return OperationFailure (implicit-converts to OperationResult<TValue>)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Creates a <see cref="OperationStatus.BadRequest"/> failure.</summary>
    /// <param name="detail">Per-occurrence explanation. Required.</param>
    /// <param name="type">Override the default <c>"bad_request"</c> type identifier.</param>
    /// <param name="errors">Optional field-keyed validation errors.</param>
    /// <param name="extensions">Optional structured extras for the problem.</param>
    /// <param name="instance">Optional URI identifying this specific occurrence.</param>
    public static OperationFailure BadRequest(
        string detail,
        string? type = null,
        IReadOnlyDictionary<string, IReadOnlyList<OperationError>>? errors = null,
        IReadOnlyDictionary<string, object?>? extensions = null,
        string? instance = null)
        => Build(OperationStatus.BadRequest, type ?? "bad_request", detail, errors, extensions, instance);

    /// <summary>Creates an <see cref="OperationStatus.Unauthorized"/> failure.</summary>
    public static OperationFailure Unauthorized(
        string detail,
        string? type = null,
        IReadOnlyDictionary<string, object?>? extensions = null,
        string? instance = null)
        => Build(OperationStatus.Unauthorized, type ?? "unauthorized", detail, errors: null, extensions, instance);

    /// <summary>Creates a <see cref="OperationStatus.Forbidden"/> failure.</summary>
    public static OperationFailure Forbidden(
        string detail,
        string? type = null,
        IReadOnlyDictionary<string, object?>? extensions = null,
        string? instance = null)
        => Build(OperationStatus.Forbidden, type ?? "forbidden", detail, errors: null, extensions, instance);

    /// <summary>Creates a <see cref="OperationStatus.NotFound"/> failure.</summary>
    public static OperationFailure NotFound(
        string detail,
        string? type = null,
        IReadOnlyDictionary<string, object?>? extensions = null,
        string? instance = null)
        => Build(OperationStatus.NotFound, type ?? "not_found", detail, errors: null, extensions, instance);

    /// <summary>Creates a <see cref="OperationStatus.Conflict"/> failure.</summary>
    public static OperationFailure Conflict(
        string detail,
        string? type = null,
        IReadOnlyDictionary<string, IReadOnlyList<OperationError>>? errors = null,
        IReadOnlyDictionary<string, object?>? extensions = null,
        string? instance = null)
        => Build(OperationStatus.Conflict, type ?? "conflict", detail, errors, extensions, instance);

    /// <summary>Creates a failure result from an <see cref="System.Exception"/>.</summary>
    public static OperationFailure Failed(
        Exception exception,
        OperationStatus status = OperationStatus.InternalServerError)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new OperationFailure
        {
            Status = status,
            Exception = exception,
            Problem = new OperationProblem
            {
                Type = "internal_error",
                Title = StatusTitle(status),
                Detail = exception.Message,
            },
        };
    }

    /// <summary>
    /// Creates a failure result with the supplied status, type, title, detail
    /// and optional structured extras. Use this when no purpose-built helper
    /// fits.
    /// </summary>
    public static OperationFailure Failure(
        OperationStatus status,
        string type,
        string title,
        string detail,
        IReadOnlyDictionary<string, IReadOnlyList<OperationError>>? errors = null,
        IReadOnlyDictionary<string, object?>? extensions = null,
        string? instance = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(detail);
        return new OperationFailure
        {
            Status = status,
            Problem = new OperationProblem
            {
                Type = type,
                Title = title,
                Detail = detail,
                Errors = errors,
                Extensions = extensions,
                Instance = instance,
            },
        };
    }

    private static OperationFailure Build(
        OperationStatus status,
        string type,
        string detail,
        IReadOnlyDictionary<string, IReadOnlyList<OperationError>>? errors,
        IReadOnlyDictionary<string, object?>? extensions,
        string? instance)
    {
        ArgumentNullException.ThrowIfNull(detail);
        return new OperationFailure
        {
            Status = status,
            Problem = new OperationProblem
            {
                Type = type,
                Title = StatusTitle(status),
                Detail = detail,
                Errors = errors,
                Extensions = extensions,
                Instance = instance,
            },
        };
    }

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
