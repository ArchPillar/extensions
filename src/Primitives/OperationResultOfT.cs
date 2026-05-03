using System.Text.Json.Serialization;

namespace ArchPillar.Extensions.Primitives;

/// <summary>
/// An <see cref="OperationResult"/> that carries a successful payload.
/// </summary>
/// <typeparam name="TValue">The payload type returned on success.</typeparam>
public sealed class OperationResult<TValue> : OperationResult
{
    /// <summary>
    /// Initializes a new <see cref="OperationResult{TValue}"/>. Prefer the
    /// static factories at call sites.
    /// </summary>
    public OperationResult()
    {
    }

    /// <summary>The payload returned on success. <c>default</c> on failure.</summary>
    [JsonPropertyName("value")]
    public TValue? Value { get; init; }

    /// <summary>
    /// Returns <see cref="Value"/> on success; throws
    /// <see cref="OperationException"/> on failure.
    /// </summary>
    /// <returns>The non-null payload.</returns>
    public new TValue Unwrap()
    {
        ThrowIfFailed();
        return Value!;
    }

    /// <summary>Wraps this typed result in a completed <see cref="Task{TResult}"/>.</summary>
    public static implicit operator Task<OperationResult<TValue>>(OperationResult<TValue> result)
        => Task.FromResult(result);

    /// <summary>Wraps a value in a successful <see cref="OperationStatus.Ok"/> result.</summary>
    public static implicit operator OperationResult<TValue>(TValue value)
        => new() { Status = OperationStatus.Ok, Value = value };

    /// <summary>Creates a successful result carrying <paramref name="value"/>.</summary>
    public static OperationResult<TValue> Ok(TValue value, OperationStatus status = OperationStatus.Ok)
        => new() { Status = status, Value = value };

    /// <summary>Creates a successful <see cref="OperationStatus.Created"/> result carrying <paramref name="value"/>.</summary>
    public static OperationResult<TValue> Created(TValue value)
        => new() { Status = OperationStatus.Created, Value = value };

    /// <summary>Creates a typed <see cref="OperationStatus.NotFound"/> failure.</summary>
    public static new OperationResult<TValue> NotFound(string? detail = null)
        => Failure(OperationStatus.NotFound, "not_found", "Not Found", detail);

    /// <summary>Creates a typed <see cref="OperationStatus.Conflict"/> failure.</summary>
    public static new OperationResult<TValue> Conflict(string? detail = null)
        => Failure(OperationStatus.Conflict, "conflict", "Conflict", detail);

    /// <summary>Creates a typed <see cref="OperationStatus.BadRequest"/> failure.</summary>
    public static new OperationResult<TValue> BadRequest(string? detail = null)
        => Failure(OperationStatus.BadRequest, "bad_request", "Bad Request", detail);

    /// <summary>Creates a typed <see cref="OperationStatus.Unauthorized"/> failure.</summary>
    public static new OperationResult<TValue> Unauthorized(string? detail = null)
        => Failure(OperationStatus.Unauthorized, "unauthorized", "Unauthorized", detail);

    /// <summary>Creates a typed <see cref="OperationStatus.Forbidden"/> failure.</summary>
    public static new OperationResult<TValue> Forbidden(string? detail = null)
        => Failure(OperationStatus.Forbidden, "forbidden", "Forbidden", detail);

    /// <summary>Creates a typed failure result from an <see cref="System.Exception"/>.</summary>
    public static new OperationResult<TValue> Failed(Exception exception, OperationStatus status = OperationStatus.InternalServerError)
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

    /// <summary>Creates a typed failure result with the supplied status, type, title, and detail.</summary>
    public static new OperationResult<TValue> Failure(OperationStatus status, string type, string title, string? detail = null)
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
}
