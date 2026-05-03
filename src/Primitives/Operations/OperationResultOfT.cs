using System.Text.Json.Serialization;

namespace ArchPillar.Extensions.Operations;

/// <summary>
/// An <see cref="OperationResult"/> that carries a successful payload.
/// </summary>
/// <remarks>
/// Construction goes through factories on <see cref="OperationResult"/>
/// (<see cref="OperationResult.Ok{TValue}(TValue)"/>,
/// <see cref="OperationResult.Created{TValue}(TValue)"/>) for success, or any
/// <see cref="OperationFailure"/>-returning factory (
/// <see cref="OperationResult.NotFound(string, string?, IReadOnlyDictionary{string, object?}?, string?)"/>,
/// <see cref="OperationResult.Conflict(string, string?, IReadOnlyDictionary{string, IReadOnlyList{OperationError}}?, IReadOnlyDictionary{string, object?}?, string?)"/>,
/// …) for failure — the implicit conversions on this type pick up the values
/// without requiring the caller to repeat <typeparamref name="TValue"/>.
/// </remarks>
/// <typeparam name="TValue">The payload type returned on success.</typeparam>
public sealed class OperationResult<TValue> : OperationResult
{
    /// <summary>
    /// Initializes a new <see cref="OperationResult{TValue}"/>. Prefer the
    /// <see cref="OperationResult"/> factories at call sites.
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

    /// <summary>
    /// Lifts a known-failure <see cref="OperationFailure"/> into a typed
    /// result so failure factories can be returned without repeating
    /// <typeparamref name="TValue"/>.
    /// </summary>
    public static implicit operator OperationResult<TValue>(OperationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new OperationResult<TValue>
        {
            Status = failure.Status,
            Problem = failure.Problem,
            Exception = failure.Exception,
        };
    }
}
