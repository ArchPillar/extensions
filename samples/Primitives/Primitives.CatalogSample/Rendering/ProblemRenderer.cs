using ArchPillar.Extensions.Operations;
using Spectre.Console;

namespace Primitives.CatalogSample.Rendering;

// Keeps Program.cs thin: all Spectre.Console formatting of an OperationProblem and
// of a success line lives here. Dynamic strings go through Markup.Escape so a stray
// '[' in domain data never breaks markup parsing.
internal static class ProblemRenderer
{
    public static void RenderSuccess(string label, OperationResult result, string? value = null)
    {
        var detail = value is null ? string.Empty : $" [grey]{Markup.Escape(value)}[/]";
        AnsiConsole.MarkupLine(
            $"  [green]OK[/] {Markup.Escape(label)} [grey](status {(int)result.Status})[/]{detail}");
    }

    public static void RenderProblem(string label, OperationResult result)
    {
        OperationProblem? problem = result.Problem;
        AnsiConsole.MarkupLine(
            $"  [red]FAIL[/] {Markup.Escape(label)} [grey](status {(int)result.Status})[/]");

        if (problem is null)
        {
            return;
        }

        AnsiConsole.MarkupLine(
            $"    type=[yellow]{Markup.Escape(problem.Type ?? "?")}[/] " +
            $"title=[yellow]{Markup.Escape(problem.Title ?? "?")}[/]");
        AnsiConsole.MarkupLine($"    detail: {Markup.Escape(problem.Detail ?? string.Empty)}");

        RenderExtensions("    ", problem.Extensions);
        RenderFieldErrors(problem.Errors);
    }

    private static void RenderFieldErrors(
        IReadOnlyDictionary<string, IReadOnlyList<OperationError>>? errors)
    {
        if (errors is null || errors.Count == 0)
        {
            return;
        }

        foreach ((var field, IReadOnlyList<OperationError> fieldErrors) in errors)
        {
            foreach (OperationError error in fieldErrors)
            {
                AnsiConsole.MarkupLine(
                    $"    [cyan]{Markup.Escape(field)}[/]: " +
                    $"[yellow]{Markup.Escape(error.Type)}[/] — {Markup.Escape(error.Detail)}");
                RenderExtensions("      ", error.Extensions);
            }
        }
    }

    private static void RenderExtensions(string indent, IReadOnlyDictionary<string, object?>? extensions)
    {
        if (extensions is null || extensions.Count == 0)
        {
            return;
        }

        foreach ((var key, var value) in extensions)
        {
            AnsiConsole.MarkupLine(
                $"{indent}[grey]{Markup.Escape(key)}=" +
                $"{Markup.Escape(value?.ToString() ?? "null")}[/]");
        }
    }
}
