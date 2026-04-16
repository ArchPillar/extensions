using System.Diagnostics;

namespace ArchPillar.Extensions.Pipelines;

/// <summary>
/// Holds the library-owned <see cref="ActivitySource"/> used by
/// <see cref="ActivityMiddleware{T}"/>. Subscribers (OpenTelemetry, raw
/// <see cref="ActivityListener"/>) should reference <see cref="Name"/> to
/// activate tracing.
/// </summary>
/// <example>
/// With OpenTelemetry:
/// <code>
/// builder.Services.AddOpenTelemetry().WithTracing(b => b
///     .AddSource(PipelineActivitySource.Name)
///     .AddOtlpExporter());
/// </code>
/// With a raw listener:
/// <code>
/// var listener = new ActivityListener
/// {
///     ShouldListenTo = s => s.Name == PipelineActivitySource.Name,
///     Sample = (ref ActivityCreationOptions&lt;ActivityContext&gt; _) =>
///         ActivitySamplingResult.AllData,
///     ActivityStopped = a =&gt; Console.WriteLine($"{a.DisplayName}: {a.Duration}"),
/// };
/// ActivitySource.AddActivityListener(listener);
/// </code>
/// </example>
public static class PipelineActivitySource
{
    /// <summary>
    /// The name of the <see cref="ActivitySource"/> used by
    /// <see cref="ActivityMiddleware{T}"/>.
    /// </summary>
    public const string Name = "ArchPillar.Extensions.Pipelines";

    internal static readonly ActivitySource Instance = new(
        Name,
        typeof(PipelineActivitySource).Assembly.GetName().Version?.ToString());
}
