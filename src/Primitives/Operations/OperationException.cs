namespace ArchPillar.Extensions.Operations;

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
    /// <summary>Wraps an existing <see cref="OperationResult"/>.</summary>
    public OperationException(OperationResult result)
        : base(BuildMessage(result), result?.Exception)
    {
        ArgumentNullException.ThrowIfNull(result);
        Result = result;
    }

    /// <summary>
    /// Constructs a result from <paramref name="status"/> and <paramref name="detail"/>
    /// and wraps it.
    /// </summary>
    public OperationException(OperationStatus status, string detail)
        : this(OperationResult.Failure(status, status.ToString().ToLowerInvariant(), OperationResult.StatusTitle(status), detail))
    {
    }

    /// <summary>The carried <see cref="OperationResult"/>.</summary>
    public OperationResult Result { get; }

    private static string BuildMessage(OperationResult? result)
    {
        if (result is null)
        {
            return "Operation failed.";
        }

        if (result.Problem?.Detail is { } detail)
        {
            return $"Operation failed with status {result.Status}: {detail}";
        }

        if (result.Problem?.Title is { } title)
        {
            return $"Operation failed with status {result.Status}: {title}";
        }

        return $"Operation failed with status {result.Status}.";
    }
}
