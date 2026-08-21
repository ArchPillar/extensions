using Spectre.Console;

namespace ArchPillar.Extensions.Localization.Tooling.Internal;

/// <summary>
/// The tool's console conventions. Progress and success are rich output on stdout (Spectre's default, cached
/// console); errors, drift, and warnings are plain text on stderr, so they survive a redirected stdout and honor a
/// redirected <see cref="Console.Error"/> (piping, tests). The exit codes follow the diff/check convention so a CI
/// gate can tell them apart: 0 success, 1 the catalog drifted (an expected outcome of <c>sync --check</c>), 2 an
/// error (bad invocation, missing or malformed file). <c>--verbose</c> adds a log on stderr — the tool's own
/// running commentary, and the output of anything it shells out to — which is off by default and silent then.
/// <para>
/// This is also the one place a runtime value crosses into Spectre's markup parser, which reads <c>[</c> as the
/// start of a style tag. Every message here is data — an assembly name, a culture, a zip entry from a translator —
/// so all of it is escaped and none of it may carry markup. Commands must not call <see cref="AnsiConsole"/> with a
/// string of their own; a name like <c>App.de[1].arb</c> would otherwise be parsed as a tag and throw.
/// </para>
/// </summary>
internal static class ToolConsole
{
    /// <summary>Whether the verbose log is on — for a caller that would otherwise build a message to throw
    /// away.</summary>
    public static bool IsVerbose { get; private set; }

    /// <summary>Turns the verbose log on. Set once, at the composition root, from <c>--verbose</c>: the console is
    /// one process-wide sink and every writer shares its verbosity, rather than each carrying its own copy of the
    /// flag down through call sites that have nothing else to say about logging.</summary>
    public static void EnableVerbose() => IsVerbose = true;

    /// <summary>Writes a line to the verbose log, or nothing when <c>--verbose</c> is off. The log goes to stderr,
    /// like the other non-data output, so it never mixes into what stdout carries.</summary>
    public static void Verbose(string message)
    {
        if (IsVerbose)
        {
            Console.Error.WriteLine(message);
        }
    }

    /// <summary>Reports a completed operation on stdout.</summary>
    public static void Success(string message) =>
        AnsiConsole.MarkupLineInterpolated($"[green]✓[/] {message}");

    /// <summary>Runs <paramref name="action"/> under a status spinner labelled <paramref name="status"/>.</summary>
    public static Task StatusAsync(string status, Func<StatusContext, Task> action) =>
        AnsiConsole.Status().StartAsync(Markup.Escape(status), action);

    /// <summary>The value-returning form of the status spinner.</summary>
    public static Task<T> StatusAsync<T>(string status, Func<StatusContext, Task<T>> action) =>
        AnsiConsole.Status().StartAsync(Markup.Escape(status), action);

    /// <summary>Re-labels a running spinner, for progress through a multi-item operation.</summary>
    public static void Status(StatusContext context, string text) =>
        context.Status = Markup.Escape(text);

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
