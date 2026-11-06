using System.Diagnostics;

namespace ArchPillar.Extensions.Localization.Tooling.Tests;

// What a test needs to drive the real SDK: the repository it runs in, and the dotnet host.
internal static class TestToolchain
{
    // The repository root, found by walking up from the test binary to the directory holding the solution.
    public static string RepositoryRoot()
    {
        for (var directory = AppContext.BaseDirectory; directory is not null; directory = Path.GetDirectoryName(directory))
        {
            if (Directory.EnumerateFiles(directory, "*.slnx").Any())
            {
                return directory;
            }
        }

        throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }

    // Runs dotnet to completion, returning its exit code and everything it printed.
    public static async Task<(int ExitCode, string Output)> RunDotnetAsync(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start the dotnet host.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await output + await error);
    }
}
