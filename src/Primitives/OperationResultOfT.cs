namespace ArchPillar.Extensions.Primitives;

/// <summary>
/// An <see cref="OperationResult"/> that carries a successful payload.
/// </summary>
/// <typeparam name="TValue">The payload type returned on success.</typeparam>
/// <remarks>
/// On a failure result <see cref="Value"/> is <c>default</c>. Callers should
/// inspect <see cref="OperationResult.IsSuccess"/> before dereferencing
/// <see cref="Value"/>.
/// </remarks>
public sealed class OperationResult<TValue> : OperationResult
{
    /// <summary>
    /// Initializes a new <see cref="OperationResult{TValue}"/>. Prefer the static
    /// factories at call sites.
    /// </summary>
    public OperationResult()
    {
    }

    /// <summary>
    /// The payload returned on success. <c>default</c> on failure.
    /// </summary>
    public TValue? Value { get; init; }

    /// <summary>
    /// Wraps this typed result in a completed <see cref="Task{TResult}"/>.
    /// </summary>
    /// <param name="result">The result to wrap.</param>
    public static implicit operator Task<OperationResult<TValue>>(OperationResult<TValue> result)
        => Task.FromResult(result);

    /// <summary>
    /// Wraps a value in a successful <see cref="OperationResult{TValue}"/> with
    /// status <see cref="OperationStatus.Ok"/>.
    /// </summary>
    /// <param name="value">The successful payload.</param>
    public static implicit operator OperationResult<TValue>(TValue value)
        => new() { Status = OperationStatus.Ok, Value = value };

    /// <summary>Creates a successful result carrying <paramref name="value"/>.</summary>
    public static OperationResult<TValue> Ok(TValue value, OperationStatus status = OperationStatus.Ok)
        => new() { Status = status, Value = value };

    /// <summary>Creates a successful <see cref="OperationStatus.Created"/> result carrying <paramref name="value"/>.</summary>
    public static OperationResult<TValue> Created(TValue value)
        => new() { Status = OperationStatus.Created, Value = value };

    /// <summary>Creates a typed <see cref="OperationStatus.NotFound"/> failure.</summary>
    public static new OperationResult<TValue> NotFound(string? message = null)
        => new()
        {
            Status = OperationStatus.NotFound,
            Errors = message is null ? [] : [new OperationError("not_found", message)],
        };

    /// <summary>Creates a typed <see cref="OperationStatus.Conflict"/> failure.</summary>
    public static new OperationResult<TValue> Conflict(string? message = null)
        => new()
        {
            Status = OperationStatus.Conflict,
            Errors = message is null ? [] : [new OperationError("conflict", message)],
        };

    /// <summary>Creates a typed <see cref="OperationStatus.BadRequest"/> failure.</summary>
    public static new OperationResult<TValue> BadRequest(params OperationError[] errors)
        => new() { Status = OperationStatus.BadRequest, Errors = errors };

    /// <summary>Creates a typed <see cref="OperationStatus.UnprocessableEntity"/> failure.</summary>
    public static new OperationResult<TValue> ValidationFailed(IReadOnlyList<OperationError> errors)
        => new() { Status = OperationStatus.UnprocessableEntity, Errors = errors };

    /// <summary>Creates a typed failure result from an <see cref="System.Exception"/>.</summary>
    public static new OperationResult<TValue> Failed(Exception exception, OperationStatus status = OperationStatus.InternalServerError)
        => new() { Status = status, Exception = exception };

    /// <summary>Creates a typed failure result with the supplied status and message.</summary>
    public static new OperationResult<TValue> Failed(OperationStatus status, string message)
        => new() { Status = status, Errors = [new OperationError("error", message)] };
}
