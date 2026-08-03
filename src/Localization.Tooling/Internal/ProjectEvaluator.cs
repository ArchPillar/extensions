using System.Diagnostics;
using System.Text.Json;

namespace ArchPillar.Extensions.Localization.Tooling.Internal;

/// <summary>What a project actually builds, as MSBuild itself evaluates it.</summary>
/// <param name="AssemblyFileName">The output assembly's file name (<c>TargetFileName</c>), e.g. <c>Acme.Web.dll</c>.</param>
/// <param name="OutputRoot">The project-relative output root (<c>BaseOutputPath</c>, normally <c>bin</c>), which a
/// project may relocate.</param>
internal sealed record ProjectOutputs(string AssemblyFileName, string OutputRoot);

/// <summary>
/// Asks MSBuild what a project builds, rather than inferring it from the project file. The assembly name is not
/// derivable by reading XML: it can come from <c>Directory.Build.props</c>, a property expression
/// (<c>$(MSBuildProjectName).Core</c>), a conditioned property group, or an import — and <c>TargetName</c> /
/// <c>TargetExt</c> can rename the file after that. Only an evaluation knows, so one is run per project (SDK 8+
/// <c>-getProperty</c>, which evaluates without building) and the answers are reused for the whole scan.
/// <para>
/// A project that cannot be evaluated — no SDK on the path, an unrestored or malformed project — yields
/// <see langword="null"/> and the caller falls back to reading the project file, so a scan degrades rather than
/// failing.
/// </para>
/// </summary>
internal static class ProjectEvaluator
{
    private const int TimeoutMilliseconds = 60_000;

    /// <summary>
    /// Evaluates every project, in parallel — each is an out-of-process MSBuild evaluation of roughly a second, so
    /// a solution-sized scan pays that once overall rather than once per project in sequence.
    /// </summary>
    public static IReadOnlyDictionary<string, ProjectOutputs?> EvaluateAll(IReadOnlyList<string> projectPaths)
    {
        var results = new Dictionary<string, ProjectOutputs?>(StringComparer.OrdinalIgnoreCase);
        if (projectPaths.Count == 0)
        {
            return results;
        }

        var evaluated = new ProjectOutputs?[projectPaths.Count];
        Parallel.For(0, projectPaths.Count, index => evaluated[index] = Evaluate(projectPaths[index]));
        for (var index = 0; index < projectPaths.Count; index++)
        {
            results[projectPaths[index]] = evaluated[index];
        }

        return results;
    }

    private static ProjectOutputs? Evaluate(string projectPath)
    {
        IReadOnlyDictionary<string, string>? properties = GetProperties(projectPath, targetFramework: null);
        if (properties is null)
        {
            return null;
        }

        var fileName = Value(properties, "TargetFileName");
        if (fileName.Length == 0)
        {
            // A multi-targeting project's outer build defines no TargetFileName — it exists once per framework.
            // Pinning any one of them yields the name, which does not vary by framework.
            var first = Value(properties, "TargetFrameworks").Split(';').FirstOrDefault(value => value.Trim().Length > 0);
            if (first is null)
            {
                return null;
            }

            properties = GetProperties(projectPath, first.Trim());
            if (properties is null)
            {
                return null;
            }

            fileName = Value(properties, "TargetFileName");
        }

        if (fileName.Length == 0)
        {
            return null;
        }

        // MSBuild reports paths in its own convention (a trailing separator, and '\' even on Unix), so normalize
        // before this is combined with anything.
        var outputRoot = Value(properties, "BaseOutputPath").Replace('\\', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
        return new ProjectOutputs(fileName, outputRoot.Length == 0 ? "bin" : outputRoot);
    }

    private static IReadOnlyDictionary<string, string>? GetProperties(string projectPath, string? targetFramework)
    {
        var start = new ProcessStartInfo(DotnetHost())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!
        };
        start.ArgumentList.Add("msbuild");
        start.ArgumentList.Add(projectPath);
        start.ArgumentList.Add("-getProperty:TargetFileName");
        start.ArgumentList.Add("-getProperty:TargetFrameworks");
        start.ArgumentList.Add("-getProperty:BaseOutputPath");
        if (targetFramework is not null)
        {
            start.ArgumentList.Add("-p:TargetFramework=" + targetFramework);
        }

        try
        {
            using Process? process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            if (!process.WaitForExit(TimeoutMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            return process.ExitCode == 0 ? Parse(output) : null;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // No dotnet on the path, or it could not be launched: fall back to reading the project file.
            return null;
        }
    }

    // The SDK sets DOTNET_HOST_PATH for the exact host running the build; outside a build, the one on the path.
    private static string DotnetHost() =>
        Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet";

    private static IReadOnlyDictionary<string, string>? Parse(string output)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(output);
            if (!document.RootElement.TryGetProperty("Properties", out JsonElement properties))
            {
                return null;
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonProperty property in properties.EnumerateObject())
            {
                values[property.Name] = property.Value.GetString() ?? string.Empty;
            }

            return values;
        }
        catch (JsonException)
        {
            // An older SDK without -getProperty, or a project whose evaluation printed diagnostics instead.
            return null;
        }
    }

    private static string Value(IReadOnlyDictionary<string, string> properties, string name) =>
        properties.TryGetValue(name, out var value) ? value.Trim() : string.Empty;
}
