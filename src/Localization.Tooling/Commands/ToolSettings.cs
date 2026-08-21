using System.ComponentModel;
using Spectre.Console.Cli;

namespace ArchPillar.Extensions.Localization.Tooling.Commands;

/// <summary>
/// What every command accepts, whatever it operates on. Verbosity is the only such option so far, and it is
/// declared once here rather than per command: it belongs to every command's help, and it is applied in one place
/// (the interceptor in <c>ToolApplication</c>) rather than by each command in turn, which would be nine chances to
/// forget.
/// </summary>
internal abstract class ToolSettings : CommandSettings
{
    [CommandOption("--verbose")]
    [Description("Log what the tool is doing on stderr, including the output of the MSBuild evaluation.")]
    public bool Verbose { get; init; }
}
