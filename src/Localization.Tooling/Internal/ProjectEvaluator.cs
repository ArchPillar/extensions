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

/// <summary>The evaluation's result: what each project builds, plus whatever MSBuild reported while working it
/// out. The diagnostics matter because a project that fails to evaluate fails the whole task — so they are the
/// only thing that says <em>which</em> project, and why.</summary>
/// <param name="Outputs">The projects that evaluated, keyed by full path.</param>
/// <param name="Diagnostics">MSBuild's error output, empty when it had none.</param>
/// <param name="Fault">Set when the <em>evaluation itself</em> failed — MSBuild could not be started, or its
/// result could not be read — as opposed to a project failing to evaluate. The two are different faults and
/// read as opposite things: the first says nothing about the projects in scope, so blaming them for it sends
/// the reader to look for a broken project that does not exist.</param>
internal sealed record ProjectEvaluation(IReadOnlyDictionary<string, ProjectOutputs> Outputs, string Diagnostics, string? Fault = null);

/// <summary>One MSBuild invocation's captured output.</summary>
/// <param name="Output">Standard output, held whole: with <c>-getTargetResult</c> this is the result payload,
/// parsed as a single JSON document, so a truncated one is no document at all.</param>
/// <param name="Errors">Standard error, bounded: this one is a log, written for a human to read.</param>
internal sealed record MsBuildRun(string Output, string Errors);

/// <summary>
/// Asks MSBuild what a project builds, rather than inferring it from the project file. The assembly name is not
/// derivable by reading XML: it can come from <c>Directory.Build.props</c>, a property expression
/// (<c>$(MSBuildProjectName).Core</c>), a conditioned property group, or an import — and <c>TargetName</c> /
/// <c>TargetExt</c> can rename the file after that. Only an evaluation knows.
/// <para>
/// Evaluation is cheap; <em>starting MSBuild</em> is not — of a one-project run, about 0.7s is process startup
/// and only 0.4s the evaluation. So every in-scope project is evaluated in a <b>single</b> MSBuild process,
/// through a generated traversal project: evaluating this repository's 57 projects costs about 2.5s that way
/// against about 17s one process at a time.
/// </para>
/// <para>
/// A project that will not evaluate is reported, not worked around. It is the same project that will not build,
/// and a project that cannot build has no assembly to scan — so the only thing a fallback could do is guess a
/// name for a file that does not exist, and guess silently. Scanning assemblies with no project in sight is what
/// <c>--input</c> and <c>--assembly</c> are for; they never come here.
/// </para>
/// </summary>
internal static class ProjectEvaluator
{
    private const int TimeoutMilliseconds = 120_000;
    private const int DiagnosticsLimit = 8000;

    private const string UnreadableOutput =
        "MSBuild ran, but its evaluation output could not be read, so no project's assembly name could be "
        + "determined. This says nothing about the projects in scope — it is a fault in the tool, or an MSBuild "
        + "that does not support -getTargetResult. Please report it.";

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

    /// <summary>
    /// The outputs of every project that could be evaluated, keyed by project path. A project missing from the
    /// result could not be evaluated — which is the same thing as not being buildable, so the caller reports it
    /// rather than guessing a name for an assembly that cannot exist.
    /// </summary>
    public static ProjectEvaluation EvaluateAll(IReadOnlyList<string> projectPaths)
    {
        var results = new Dictionary<string, ProjectOutputs>(StringComparer.OrdinalIgnoreCase);
        if (projectPaths.Count == 0)
        {
            return new ProjectEvaluation(results, string.Empty);
        }

        (IEnumerable<KeyValuePair<string, ProjectOutputs>> evaluated, var diagnostics, var fault) = EvaluateBatch(projectPaths);
        foreach ((var path, ProjectOutputs outputs) in evaluated)
        {
            results[path] = outputs;
        }

        return new ProjectEvaluation(results, diagnostics, fault);
    }

    private static (IEnumerable<KeyValuePair<string, ProjectOutputs>> Evaluated, string Diagnostics, string? Fault) EvaluateBatch(IReadOnlyList<string> projectPaths)
    {
        var workspace = Path.Combine(Path.GetTempPath(), "apl-eval-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(workspace);
            var targets = Path.Combine(workspace, "collect.targets");
            var traversal = Path.Combine(workspace, "collect.proj");
            File.WriteAllText(targets, CollectTargets);
            File.WriteAllText(traversal, Traversal(projectPaths, targets));

            MsBuildRun? run = RunMsBuild(workspace, [traversal, "-t:ArchPillarAplGetOutputs", "-getTargetResult:ArchPillarAplGetOutputs"]);
            if (run is null)
            {
                return ([], string.Empty, "MSBuild could not be run. Is the .NET SDK installed and on the path?");
            }

            // Parsed whatever the exit code: one project that cannot be evaluated fails the whole task, but the
            // projects that DID evaluate still come back on stdout. Discarding them because the run exited
            // non-zero would report every project in the scope as broken because one of them is.
            (IEnumerable<KeyValuePair<string, ProjectOutputs>> evaluated, var fault) = ParseItems(run.Output);
            return (evaluated, run.Errors, fault);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return ([], string.Empty, "The temporary workspace this evaluation runs in could not be created: " + error.Message);
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
            builder.Append("    <ArchPillarAplProject Include=\"").Append(Escape(Path.GetFullPath(path))).Append("\" />\n");
        }

        // Deliberately NOT BuildInParallel: that spawns MSBuild worker nodes, and several tool invocations running
        // at once (a parallel build, a busy CI agent) then fail their node handshake. Evaluating in one node is
        // barely slower, since the cost here is process startup, not the evaluation.
        // ContinueOnError lets the projects that DO evaluate come back even when one of them cannot (the task
        // still reports failure, which is why the output is read regardless of exit code).
        // SkipNonexistentProjects covers a solution that lists a project that has been deleted.
        var escapedTargets = Escape(targets);
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

    // An output that will not parse is a fault of its own, and never "the projects are broken": no project's
    // outputs come back either way, so the two are indistinguishable from the result alone — which is exactly why
    // they must be told apart here, where the difference is still known.
    private static (IEnumerable<KeyValuePair<string, ProjectOutputs>> Evaluated, string? Fault) ParseItems(string output)
    {
        var results = new List<KeyValuePair<string, ProjectOutputs>>();
        try
        {
            using JsonDocument document = JsonDocument.Parse(output);
            if (!document.RootElement.TryGetProperty("TargetResults", out JsonElement targetResults)
                || !targetResults.TryGetProperty("ArchPillarAplGetOutputs", out JsonElement result)
                || !result.TryGetProperty("Items", out JsonElement items))
            {
                return ([], UnreadableOutput);
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
            return ([], UnreadableOutput);
        }

        return (results, null);
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

    private static MsBuildRun? RunMsBuild(string workingDirectory, IReadOnlyList<string> arguments)
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

        // MSBuild keeps worker nodes alive for reuse, and those nodes inherit the redirected output handle — so
        // the read below would block on them long after the evaluation finished. They are worthless here (nothing
        // is ever built twice), so switch reuse off and stay in a single node.
        start.ArgumentList.Add("-nodeReuse:false");
        start.ArgumentList.Add("-m:1");
        // MSBuild otherwise auto-includes a Directory.Build.rsp found by walking up from the entry project — which
        // lives in the temp directory. A response file there is nobody's intent, belongs to whoever can write to
        // temp, and can carry anything up to -logger:<assembly>.
        start.ArgumentList.Add("-noAutoResponse");
        start.ArgumentList.Add("-nologo");

        try
        {
            using Process? process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            var output = new StringBuilder();
            var errors = new StringBuilder();
            process.OutputDataReceived += (_, args) => AppendPayload(output, args.Data);
            process.ErrorDataReceived += (_, args) => AppendDiagnostic(errors, args.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!process.WaitForExit(TimeoutMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            // The timed overload returns as soon as the process exits, WITHOUT waiting for the asynchronous
            // readers to deliver what it wrote. Only the parameterless overload does — omit it and a loaded
            // machine yields empty output, which would read as "nothing evaluated". Safe from hanging because
            // node reuse is off: nothing outlives the exit.
            process.WaitForExit();
            return new MsBuildRun(output.ToString(), errors.ToString());
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // dotnet is not on the path, or could not be launched. The caller turns this into a diagnostic.
            return null;
        }
    }

    // Unbounded, because stdout here is not a log: it is one JSON document, and every project's outputs are in
    // it. Cut it at any length and it stops being a document — which reads as "no project evaluated", so the
    // scope reports every project in it as unevaluable, with no diagnostic to say why. Its size is the scope's
    // size (roughly 1.7 KB a project), so it is the caller's own solution that bounds it.
    private static void AppendPayload(StringBuilder builder, string? line)
    {
        if (line is not null)
        {
            builder.AppendLine(line);
        }
    }

    // Bounded, because stderr is a log: it is for a human to read, and a pathological build's must not be held
    // whole. Truncating it costs nothing — what went wrong is at the top.
    private static void AppendDiagnostic(StringBuilder builder, string? line)
    {
        if (line is not null && builder.Length < DiagnosticsLimit)
        {
            builder.AppendLine(line);
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

    // XML escaping alone is not enough: MSBuild reads its own metacharacters out of an Include or a Properties
    // value, so a directory named "a;b" would split into two item specs and one named "a%b" would decode as a hex
    // escape — both silently resolving to nothing. Percent goes first, or it would re-escape the escapes.
    private static string Escape(string value)
    {
        var escaped = value
            .Replace("%", "%25", StringComparison.Ordinal)
            .Replace(";", "%3B", StringComparison.Ordinal)
            .Replace("$", "%24", StringComparison.Ordinal)
            .Replace("@", "%40", StringComparison.Ordinal)
            .Replace("'", "%27", StringComparison.Ordinal)
            .Replace("*", "%2A", StringComparison.Ordinal)
            .Replace("?", "%3F", StringComparison.Ordinal);
        return SecurityElement.Escape(escaped);
    }

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out JsonElement value) ? value.GetString()?.Trim() ?? string.Empty : string.Empty;
}
