using System.Diagnostics;

namespace ArchPillar.Extensions.Commands;

/// <summary>
/// Holds the library-owned <see cref="ActivitySource"/> used by the command
/// dispatch pipeline's telemetry middleware. Subscribers (OpenTelemetry, raw
/// <see cref="ActivityListener"/>) reference <see cref="Name"/> to activate
/// tracing.
/// </summary>
/// <example>
/// With OpenTelemetry:
/// <code>
/// builder.Services.AddOpenTelemetry().WithTracing(b => b
///     .AddSource(CommandActivitySource.Name)
///     .AddOtlpExporter());
/// </code>
/// </example>
public static class CommandActivitySource
{
    /// <summary>
    /// The name of the <see cref="ActivitySource"/> used by the command
    /// pipeline.
    /// </summary>
    public const string Name = "ArchPillar.Extensions.Commands";

    internal static readonly ActivitySource Instance = new(
        Name,
        typeof(CommandActivitySource).Assembly.GetName().Version?.ToString());
}
