namespace ArchPillar.Extensions.Primitives;

/// <summary>
/// Async extensions for awaiting an <see cref="OperationResult"/>-returning
/// task and unwrapping it in a single call. Removes the
/// <c>(await ...).Unwrap()</c> parenthesis dance:
/// <code>
/// // Instead of:
/// var order = (await dispatcher.SendAsync(query)).Unwrap();
///
/// // Write:
/// var order = await dispatcher.SendAsync(query).UnwrapAsync();
/// </code>
/// </summary>
public static class OperationResultExtensions
{
    /// <summary>
    /// Awaits <paramref name="task"/>, then asserts the operation succeeded.
    /// Throws <see cref="OperationException"/> on failure.
    /// </summary>
    /// <param name="task">The task producing the result.</param>
    /// <returns>A task that completes when the operation succeeded.</returns>
    public static async Task UnwrapAsync(this Task<OperationResult> task)
    {
        ArgumentNullException.ThrowIfNull(task);
        OperationResult result = await task.ConfigureAwait(false);
        result.Unwrap();
    }

    /// <summary>
    /// Awaits <paramref name="task"/>, then returns the payload on success.
    /// Throws <see cref="OperationException"/> on failure.
    /// </summary>
    /// <typeparam name="TValue">The payload type.</typeparam>
    /// <param name="task">The task producing the result.</param>
    /// <returns>The non-null payload from the awaited result.</returns>
    public static async Task<TValue> UnwrapAsync<TValue>(this Task<OperationResult<TValue>> task)
    {
        ArgumentNullException.ThrowIfNull(task);
        OperationResult<TValue> result = await task.ConfigureAwait(false);
        return result.Unwrap();
    }
}
