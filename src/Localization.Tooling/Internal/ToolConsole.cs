using Spectre.Console;

namespace ArchPillar.Extensions.Localization.Tooling.Internal;

/// <summary>
/// The tool's console conventions. Progress and success are rich output on stdout (Spectre's default, cached
/// console); errors, drift, and warnings are plain text on stderr, so they survive a redirected stdout and honor a
/// redirected <see cref="Console.Error"/> (piping, tests). The exit codes follow the diff/check convention so a CI
/// gate can tell them apart: 0 success, 1 the catalog drifted (an expected outcome of <c>sync --check</c>), 2 an
/// error (bad invocation, missing or malformed file).
/// </summary>
internal static class ToolConsole
{
    /// <summary>Reports a completed operation on stdout.</summary>
    public static void Success(string message) =>
        AnsiConsole.MarkupLineInterpolated($"[green]✓[/] {message}");

    /// <summary>Writes an informational progress line on stdout.</summary>
    public static void Info(string message) =>
        AnsiConsole.WriteLine(message);

    /// <summary>Reports drift (a <c>sync --check</c> found an out-of-date catalog) on stderr; returns exit code 1.</summary>
    public static int Drift(string message)
    {
        Console.Error.WriteLine("drift: " + message);
        return 1;
    }

    /// <summary>Reports a non-fatal warning (a lossy conversion) on stderr.</summary>
    public static void Warn(string message) =>
        Console.Error.WriteLine("warning: " + message);

    /// <summary>Reports an error on stderr; returns exit code 2.</summary>
    public static int Fail(string message)
    {
        Console.Error.WriteLine("error: " + message);
        return 2;
    }
}
