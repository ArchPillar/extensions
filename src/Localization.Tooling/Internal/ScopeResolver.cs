namespace ArchPillar.Extensions.Localization.Tooling.Internal;

/// <summary>
/// How a command was told which scope to operate on — an explicit <c>Assembly</c>/<c>Input</c>, or a
/// <c>Project</c>/<c>Solution</c> to discover. Shared by both resolvers: <see cref="ScopeResolver"/> reads the
/// scope's built assemblies, <c>CatalogDirectoryResolver</c> reads its catalog directories (so the catalog-consuming
/// commands ignore <c>Assembly</c>).
/// </summary>
internal sealed record ScopeOptions(string? Assembly, string? Input, string? Project, string? Solution, bool Recurse);

/// <summary>
/// Resolves a project / solution / directory scope to the set of built assemblies that actually carry a baked
/// localization template — so the authoring commands work over a whole app at once instead of one assembly at
/// a time. A single explicit <c>--assembly</c> is honoured for the low-level path; everything else fans out
/// over a build output tree and keeps only assemblies with strings.
/// </summary>
internal static class ScopeResolver
{
    /// <summary>Returns the in-scope assembly paths, deduplicated by file name (newest build wins). Whether an
    /// assembly actually has translatable strings is decided later, when its IL is read.</summary>
    public static IReadOnlyList<string> Resolve(ScopeOptions scope)
    {
        var byName = new Dictionary<string, (string Path, DateTime Written)>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in CandidateAssemblies(scope))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            DateTime written = File.GetLastWriteTimeUtc(path);
            // Multi-targeting puts the same assembly under several TFM folders; keep one, preferring the most
            // recently built so a fresh extract never reads a stale duplicate.
            if (!byName.TryGetValue(name, out (string Path, DateTime Written) existing) || written > existing.Written)
            {
                byName[name] = (path, written);
            }
        }

        return [.. byName.Values.Select(entry => entry.Path).OrderBy(Path.GetFileNameWithoutExtension, StringComparer.Ordinal)];
    }

    private static IEnumerable<string> CandidateAssemblies(ScopeOptions scope)
    {
        if (scope.Assembly is { Length: > 0 } assembly)
        {
            return [Path.GetFullPath(assembly)];
        }

        // A project or solution names *projects*, so only those projects' own output assemblies are candidates.
        // A bin folder is mostly other people's code — every NuGet dependency and native interop library is
        // copied there — and none of it belongs to this scope. `--input` is the opposite: it names a directory
        // of built assemblies, so everything in it is a candidate by definition.
        IEnumerable<string> assemblies = scope switch
        {
            { Input: { Length: > 0 } input } => AssembliesUnder(input),
            { Project: { } project } => ProjectAssemblies(ScopeDiscovery.ProjectClosure(
                ScopeDiscovery.ResolveSingleFile(project, "project", "*.csproj"), scope.Recurse)),
            { Solution: { } solution } => ProjectAssemblies(
                ScopeDiscovery.SolutionProjects(ScopeDiscovery.ResolveSingleFile(solution, "solution", "*.sln", "*.slnx"))
                    .SelectMany(project => ScopeDiscovery.ProjectClosure(project, recurse: false))),
            _ => DiscoverInCurrentDirectory(scope.Recurse)
        };

        return assemblies.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    // With no scope at all, default to the current directory like `dotnet build`: a lone solution wins, else
    // a lone project; an ambiguous or empty directory is an error rather than a guess.
    private static IEnumerable<string> DiscoverInCurrentDirectory(bool recurse)
    {
        CurrentDirectoryScope current = ScopeDiscovery.DiscoverCurrentDirectory();
        if (current.Solution is { } solution)
        {
            return ProjectAssemblies(ScopeDiscovery.SolutionProjects(solution)
                .SelectMany(project => ScopeDiscovery.ProjectClosure(project, recurse: false)));
        }

        if (current.Project is { } project)
        {
            return ProjectAssemblies(ScopeDiscovery.ProjectClosure(project, recurse));
        }

        throw new ArgumentException("No project or solution found in the current directory. Run from your app folder, or pass --project, --solution, or --input <dir>.");
    }

    // Each project contributes only the assembly it builds. What that assembly is called is asked of MSBuild
    // rather than read off the project file — the name can come from Directory.Build.props, a property
    // expression, or an import, none of which are visible in the XML. Every project is evaluated in one batch,
    // so the scan pays for it once. The file is then found by name under the project's output root, which
    // covers every configuration and target framework built while excluding the dependencies beside them.
    private static IEnumerable<string> ProjectAssemblies(IEnumerable<string> projects)
    {
        List<string> ordered = [.. projects.Distinct(StringComparer.OrdinalIgnoreCase)];
        ProjectEvaluation evaluation = ProjectEvaluator.EvaluateAll(ordered);

        // A project that will not evaluate is the same project that will not build, and one that cannot build has
        // no assembly to scan — so say so, rather than guessing a name for a file that cannot exist and reporting
        // "no strings" for a project full of them. Only the projects that actually failed are named: one bad
        // project fails the whole MSBuild task, but the rest still evaluated and must not be blamed for it.
        List<string> unevaluated = [.. ordered.Where(project => !evaluation.Outputs.ContainsKey(project))];
        if (unevaluated.Count > 0)
        {
            throw new ArgumentException(Unevaluated(unevaluated, evaluation.Diagnostics));
        }

        return ordered.SelectMany(project => OwnAssemblies(project, evaluation.Outputs[project]));
    }

    private static string Unevaluated(List<string> projects, string diagnostics)
    {
        var named = projects.Count == 1
            ? $"project '{projects[0]}'"
            : $"{projects.Count} projects:{string.Concat(projects.Select(project => "\n  " + project))}";
        var message =
            $"Could not evaluate {named}. Every project in scope must be one MSBuild can evaluate — the same "
            + "requirement as building it — because that is where the assembly each project builds is read from. "
            + "To read built assemblies without their projects, scope with --input <dir> or --assembly <dll> instead.";

        // MSBuild's own diagnostic is the part that says what is actually wrong with the project, so it is
        // reported rather than swallowed.
        return string.IsNullOrWhiteSpace(diagnostics) ? message : message + "\n\nMSBuild reported:\n" + diagnostics.Trim();
    }

    private static IEnumerable<string> OwnAssemblies(string projectPath, ProjectOutputs outputs)
    {
        var root = Path.Combine(Path.GetDirectoryName(projectPath)!, outputs.OutputRoot);
        return Directory.Exists(root)
            ? Directory.EnumerateFiles(root, outputs.AssemblyFileName, SearchOption.AllDirectories)
            : [];
    }

    private static IEnumerable<string> AssembliesUnder(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.dll", SearchOption.AllDirectories)
            : [];
}
