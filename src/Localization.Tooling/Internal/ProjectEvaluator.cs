using System.Diagnostics;
using System.Security;
using System.Text;
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
/// <c>TargetExt</c> can rename the file after that. Only an evaluation knows.
/// <para>
/// Evaluation is cheap; <em>starting MSBuild</em> is not — of a one-project run, about 0.7s is process startup
/// and only 0.4s the evaluation. So every in-scope project is evaluated in a <b>single</b> MSBuild process,
/// through a generated traversal project: evaluating this repository's 57 projects costs about 2.5s that way
/// against about 17s one process at a time. A project the batch does not return (one malformed project fails the
/// whole task) is then evaluated on its own, and one that fails even alone falls back to reading the project
/// file — so the scan degrades in steps rather than silently resolving nothing.
/// </para>
/// </summary>
internal static class ProjectEvaluator
{
    private const int TimeoutMilliseconds = 120_000;

    // Imported into every evaluated project, so the traversal below has a target to call that exists everywhere.
    // Both hooks are needed: a multi-targeting project's outer build imports the cross-targeting targets, not the
    // common ones, and would otherwise report "target does not exist".
    private const string CollectTargets = """
        <Project>
          <Target Name="ArchPillarAplGetOutputs" Returns="@(ArchPillarAplOutput)">
            <ItemGroup>
              <ArchPillarAplOutput Include="$(MSBuildProjectFullPath)">
                <AplTargetFileName>$(TargetFileName)</AplTargetFileName>
                <AplAssemblyName>$(AssemblyName)</AplAssemblyName>
                <AplBaseOutputPath>$(BaseOutputPath)</AplBaseOutputPath>
              </ArchPillarAplOutput>
            </ItemGroup>
          </Target>
        </Project>
        """;

    /// <summary>Evaluates every project, batching them into one MSBuild process wherever possible.</summary>
    public static IReadOnlyDictionary<string, ProjectOutputs?> EvaluateAll(IReadOnlyList<string> projectPaths)
    {
        var results = new Dictionary<string, ProjectOutputs?>(StringComparer.OrdinalIgnoreCase);
        if (projectPaths.Count == 0)
        {
            return results;
        }

        foreach ((var path, ProjectOutputs outputs) in EvaluateBatch(projectPaths))
        {
            results[path] = outputs;
        }

        // Whatever the batch did not answer for — a project it could not load, or a whole batch lost to one bad
        // project — is evaluated individually rather than left unresolved.
        List<string> missing = [.. projectPaths.Where(path => !results.ContainsKey(path))];
        if (missing.Count == 0)
        {
            return results;
        }

        var evaluated = new ProjectOutputs?[missing.Count];
        Parallel.For(0, missing.Count, index => evaluated[index] = EvaluateOne(missing[index]));
        for (var index = 0; index < missing.Count; index++)
        {
            results[missing[index]] = evaluated[index];
        }

        return results;
    }

    private static IEnumerable<KeyValuePair<string, ProjectOutputs>> EvaluateBatch(IReadOnlyList<string> projectPaths)
    {
        var workspace = Path.Combine(Path.GetTempPath(), "apl-eval-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(workspace);
            var targets = Path.Combine(workspace, "collect.targets");
            var traversal = Path.Combine(workspace, "collect.proj");
            File.WriteAllText(targets, CollectTargets);
            File.WriteAllText(traversal, Traversal(projectPaths, targets));

            var output = RunMsBuild(workspace, [traversal, "-t:ArchPillarAplGetOutputs", "-getTargetResult:ArchPillarAplGetOutputs"]);
            return output is null ? [] : ParseItems(output);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        finally
        {
            Delete(workspace);
        }
    }

    private static string Traversal(IReadOnlyList<string> projectPaths, string targets)
    {
        var builder = new StringBuilder("<Project>\n  <ItemGroup>\n");
        foreach (var path in projectPaths)
        {
            builder.Append("    <ArchPillarAplProject Include=\"").Append(SecurityElement.Escape(Path.GetFullPath(path))).Append("\" />\n");
        }

        // Deliberately NOT BuildInParallel: that spawns MSBuild worker nodes, and several tool invocations running
        // at once (a parallel build, a busy CI agent) then fail their node handshake — which would degrade to the
        // guessed name silently. Evaluating in one node is barely slower, since the cost here is process startup.
        // ContinueOnError keeps a project that cannot be evaluated from failing the run; the caller then retries
        // whatever did not come back. SkipNonexistentProjects covers a solution that lists a deleted project.
        var escapedTargets = SecurityElement.Escape(targets);
        builder.Append("  </ItemGroup>\n")
            .Append("  <Target Name=\"ArchPillarAplGetOutputs\" Returns=\"@(ArchPillarAplResult)\">\n")
            .Append("    <MSBuild Projects=\"@(ArchPillarAplProject)\" Targets=\"ArchPillarAplGetOutputs\"\n")
            .Append("             SkipNonexistentProjects=\"true\" ContinueOnError=\"true\"\n")
            .Append("             Properties=\"CustomAfterMicrosoftCommonTargets=").Append(escapedTargets)
            .Append(";CustomAfterMicrosoftCommonCrossTargetingTargets=").Append(escapedTargets).Append("\">\n")
            .Append("      <Output TaskParameter=\"TargetOutputs\" ItemName=\"ArchPillarAplResult\" />\n")
            .Append("    </MSBuild>\n  </Target>\n</Project>\n");
        return builder.ToString();
    }

    private static IEnumerable<KeyValuePair<string, ProjectOutputs>> ParseItems(string output)
    {
        var results = new List<KeyValuePair<string, ProjectOutputs>>();
        JsonElement items;
        try
        {
            using JsonDocument document = JsonDocument.Parse(output);
            if (!document.RootElement.TryGetProperty("TargetResults", out JsonElement targetResults)
                || !targetResults.TryGetProperty("ArchPillarAplGetOutputs", out JsonElement result)
                || !result.TryGetProperty("Items", out items))
            {
                return results;
            }

            foreach (JsonElement item in items.EnumerateArray())
            {
                var path = Text(item, "FullPath");
                ProjectOutputs? outputs = OutputsFrom(Text(item, "AplTargetFileName"), Text(item, "AplAssemblyName"), Text(item, "AplBaseOutputPath"));
                if (path.Length > 0 && outputs is not null)
                {
                    results.Add(new KeyValuePair<string, ProjectOutputs>(path, outputs));
                }
            }
        }
        catch (JsonException)
        {
            return [];
        }

        return results;
    }

    // TargetFileName is the exact answer, but a multi-targeting project's OUTER build does not define it — the
    // file name exists once per framework. AssemblyName is evaluated there (which is the part reading XML gets
    // wrong), and every .NET SDK target framework emits a .dll, so the name is completed from it.
    private static ProjectOutputs? OutputsFrom(string targetFileName, string assemblyName, string baseOutputPath)
    {
        var fileName = targetFileName;
        if (fileName.Length == 0 && assemblyName.Length > 0)
        {
            fileName = assemblyName + ".dll";
        }

        if (fileName.Length == 0)
        {
            return null;
        }

        // MSBuild reports an output path in its own convention — a trailing separator, and '\' even on Unix.
        var root = baseOutputPath.Replace('\\', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
        return new ProjectOutputs(fileName, root.Length == 0 ? "bin" : root);
    }

    private static ProjectOutputs? EvaluateOne(string projectPath)
    {
        IReadOnlyDictionary<string, string>? properties = Properties(projectPath, targetFramework: null);
        if (properties is null)
        {
            return null;
        }

        ProjectOutputs? outputs = OutputsFrom(Value(properties, "TargetFileName"), Value(properties, "AssemblyName"), Value(properties, "BaseOutputPath"));
        if (outputs is not null)
        {
            return outputs;
        }

        // Neither name is defined in the outer build: pin one of the frameworks, where both always are.
        var first = Value(properties, "TargetFrameworks").Split(';').FirstOrDefault(value => value.Trim().Length > 0);
        if (first is null)
        {
            return null;
        }

        properties = Properties(projectPath, first.Trim());
        return properties is null
            ? null
            : OutputsFrom(Value(properties, "TargetFileName"), Value(properties, "AssemblyName"), Value(properties, "BaseOutputPath"));
    }

    private static IReadOnlyDictionary<string, string>? Properties(string projectPath, string? targetFramework)
    {
        List<string> arguments =
        [
            projectPath,
            "-getProperty:TargetFileName",
            "-getProperty:AssemblyName",
            "-getProperty:TargetFrameworks",
            "-getProperty:BaseOutputPath"
        ];
        if (targetFramework is not null)
        {
            arguments.Add("-p:TargetFramework=" + targetFramework);
        }

        var output = RunMsBuild(Path.GetDirectoryName(Path.GetFullPath(projectPath))!, arguments);
        if (output is null)
        {
            return null;
        }

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
            // An older SDK without -getProperty, or an evaluation that printed diagnostics instead.
            return null;
        }
    }

    private static string? RunMsBuild(string workingDirectory, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(DotnetHost())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory
        };
        start.ArgumentList.Add("msbuild");
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        // MSBuild keeps worker nodes alive for reuse by default, and those nodes inherit the redirected output
        // handle — so the read below would block on them long after the evaluation finished. They are worthless
        // here (nothing is ever built twice), so switch reuse off and stay in a single node.
        start.ArgumentList.Add("-nodeReuse:false");
        start.ArgumentList.Add("-m:1");
        start.ArgumentList.Add("-nologo");

        try
        {
            using Process? process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            var output = new StringBuilder();
            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data is not null)
                {
                    output.AppendLine(args.Data);
                }
            };
            process.ErrorDataReceived += (_, _) => { };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!process.WaitForExit(TimeoutMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            // The timed overload returns as soon as the process exits, WITHOUT waiting for the asynchronous
            // readers to deliver what it wrote. Only the parameterless overload does — omit it and a loaded
            // machine yields exit code 0 with empty output, which reads as "no result" and silently degrades to
            // the guessed assembly name. Safe from hanging because node reuse is off: nothing outlives the exit.
            process.WaitForExit();
            return process.ExitCode == 0 ? output.ToString() : null;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // No dotnet on the path, or it could not be launched: the caller falls back to the project file.
            return null;
        }
    }

    // The SDK sets DOTNET_HOST_PATH for the exact host running the build; outside a build, the one on the path.
    private static string DotnetHost() =>
        Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet";

    private static void Delete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is harmless; the OS reclaims it.
        }
        catch (UnauthorizedAccessException)
        {
            // As above — cleanup is best effort and never worth failing a scan over.
        }
    }

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out JsonElement value) ? value.GetString()?.Trim() ?? string.Empty : string.Empty;

    private static string Value(IReadOnlyDictionary<string, string> properties, string name) =>
        properties.TryGetValue(name, out var value) ? value.Trim() : string.Empty;
}
