using System.Diagnostics;

namespace ArchPillar.Extensions.Pipelines;

/// <summary>
/// Optional contract for <see cref="Pipeline{T}"/> contexts that want to participate in
/// distributed tracing via <see cref="ActivityMiddleware{T}"/>. Implement this interface
/// on your context type to surface a human-readable operation name and, optionally, an
/// explicit activity kind, a remote trace-context parent, and context-specific
/// activity enrichment.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Pipeline{T}"/> itself is unaware of this interface — only
/// <see cref="ActivityMiddleware{T}"/> constrains on it. Contexts that don't need
/// telemetry don't implement <see cref="IPipelineContext"/> at all.
/// </para>
/// <para>
/// All members except <see cref="OperationName"/> have default implementations, so the
/// minimum valid implementation is a single property:
/// <code>
/// public sealed class OrderContext : IPipelineContext
/// {
///     public int OrderId { get; init; }
///     public string OperationName => "Orders.Place";
/// }
/// </code>
/// </para>
/// </remarks>
public interface IPipelineContext
{
    /// <summary>
    /// The operation name used when starting the <see cref="Activity"/>. Typically
    /// describes what the pipeline invocation represents
    /// (e.g. <c>"Orders.Place"</c>, <c>"Inventory.Reserve"</c>).
    /// </summary>
    string OperationName { get; }

    /// <summary>
    /// The kind of <see cref="Activity"/> to start. Default: <see cref="ActivityKind.Internal"/>.
    /// Override to <see cref="ActivityKind.Server"/> for inbound request handlers,
    /// <see cref="ActivityKind.Consumer"/> for queue handlers,
    /// <see cref="ActivityKind.Producer"/> for outbound-message pipelines, or
    /// <see cref="ActivityKind.Client"/> for outbound-call pipelines.
    /// </summary>
    ActivityKind ActivityKind => ActivityKind.Internal;

    /// <summary>
    /// Explicit parent for the started <see cref="Activity"/>. Default is
    /// <c>default(ActivityContext)</c>, which falls back to <see cref="Activity.Current"/>.
    /// Override to inject a remote parent — for example, a <c>traceparent</c> header
    /// parsed from a queue message with
    /// <see cref="ActivityContext.TryParse(string, string, out ActivityContext)"/>.
    /// </summary>
    ActivityContext ParentContext => default;

    /// <summary>
    /// Optional hook to enrich the started activity with context-specific tags, events,
    /// or links. Called immediately after the activity is started and before
    /// <c>next(...)</c>. Default: no-op.
    /// </summary>
    /// <param name="activity">The started activity. Never <c>null</c>.</param>
    void EnrichActivity(Activity activity) { }
}
