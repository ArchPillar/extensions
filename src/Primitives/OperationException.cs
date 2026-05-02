namespace ArchPillar.Extensions.Primitives;

/// <summary>
/// Exception that carries an <see cref="OperationResult"/>. Thrown when a
/// failure is signalled via <c>throw result;</c> (using the implicit conversion
/// from <see cref="OperationResult"/> to <see cref="System.Exception"/>) or by
/// <see cref="OperationResult.ThrowIfFailed"/>.
/// </summary>
/// <remarks>
/// The dispatcher's exception middleware unwraps this exception back into the
/// carried <see cref="Result"/>. Other code may catch it to inspect the result.
/// </remarks>
public sealed class OperationException : Exception
{
    /// <summary>
    /// Wraps an existing <see cref="OperationResult"/>.
    /// </summary>
    /// <param name="result">The result to carry.</param>
    public OperationException(OperationResult result)
        : base(BuildMessage(result), result?.Exception)
    {
        ArgumentNullException.ThrowIfNull(result);
        Result = result;
    }

    /// <summary>
    /// Constructs a result from <paramref name="status"/> and <paramref name="message"/>
    /// and wraps it. Useful as a one-liner inside handlers:
    /// <c>throw new OperationException(OperationStatus.NotFound, "Order missing");</c>
    /// </summary>
    /// <param name="status">The status to carry.</param>
    /// <param name="message">An optional human-readable description.</param>
    public OperationException(OperationStatus status, string? message = null)
        : this(BuildResult(status, message))
    {
    }

    /// <summary>The carried <see cref="OperationResult"/>.</summary>
    public OperationResult Result { get; }

    private static OperationResult BuildResult(OperationStatus status, string? message)
        => message is null
            ? new OperationResult { Status = status }
            : OperationResult.Failed(status, message);

    private static string BuildMessage(OperationResult? result)
    {
        if (result is null)
        {
            return "Operation failed.";
        }

        if (result.Errors.Count > 0)
        {
            return $"Operation failed with status {result.Status}: {result.Errors[0].Message}";
        }

        return $"Operation failed with status {result.Status}.";
    }
}
