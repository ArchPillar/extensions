namespace ArchPillar.Extensions.Primitives;

/// <summary>
/// An <see cref="OperationResult"/> known at construction time to be a failure.
/// Returned from every non-success factory on <see cref="OperationResult"/>
/// (<see cref="OperationResult.NotFound"/>, <see cref="OperationResult.Conflict"/>, …).
/// </summary>
/// <remarks>
/// Pure marker subclass. Exists so the failure factories can return a value
/// that an implicit conversion on <see cref="OperationResult{TValue}"/> picks
/// up — the caller never has to repeat the typed result's <c>TValue</c> at
/// the failure site:
/// <code>
/// public Task&lt;OperationResult&lt;Order&gt;&gt; HandleAsync(...)
/// {
///     if (notFound) return OperationResult.NotFound("Order missing.");
///     // ↑ returns OperationFailure, implicitly converted to OperationResult&lt;Order&gt;.
/// }
/// </code>
/// </remarks>
public sealed class OperationFailure : OperationResult
{
}
