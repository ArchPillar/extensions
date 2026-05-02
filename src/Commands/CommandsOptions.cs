namespace ArchPillar.Extensions.Commands;

/// <summary>
/// Configuration for the command dispatcher, set via
/// <c>services.AddCommands(o =&gt; ...)</c>.
/// </summary>
public sealed class CommandsOptions
{
    /// <summary>
    /// When <c>true</c>, the registry is fully materialized at host startup
    /// (eagerly resolving every registered descriptor). Failures surface
    /// at startup instead of at first dispatch. Off by default — startup
    /// stays proportional to the number of commands actually invoked.
    /// </summary>
    public bool ValidateHandlersAtStartup { get; set; }
}
