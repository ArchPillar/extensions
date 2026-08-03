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
            { Project: { } project } => ProjectAssemblies(ScopeDiscovery.ResolveSingleFile(project, "project", "*.csproj"), scope.Recurse),
            { Solution: { } solution } => ScopeDiscovery.SolutionProjects(ScopeDiscovery.ResolveSingleFile(solution, "solution", "*.sln", "*.slnx"))
                .SelectMany(project => ProjectAssemblies(project, recurse: false)),
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
            return ScopeDiscovery.SolutionProjects(solution).SelectMany(project => ProjectAssemblies(project, recurse: false));
        }

        if (current.Project is { } project)
        {
            return ProjectAssemblies(project, recurse);
        }

        throw new ArgumentException("No project or solution found in the current directory. Run from your app folder, or pass --project, --solution, or --input <dir>.");
    }

    // Each project in the closure (the project itself, plus transitive references when recursing) contributes
    // only the assembly it builds — found by name under its bin tree, so every configuration and target
    // framework is covered while its dependencies are not.
    private static IEnumerable<string> ProjectAssemblies(string projectPath, bool recurse) =>
        ScopeDiscovery.ProjectClosure(projectPath, recurse).SelectMany(OwnAssemblies);

    private static IEnumerable<string> OwnAssemblies(string projectPath)
    {
        var bin = Path.Combine(Path.GetDirectoryName(projectPath)!, "bin");
        return Directory.Exists(bin)
            ? Directory.EnumerateFiles(bin, ScopeDiscovery.AssemblyNameOf(projectPath) + ".dll", SearchOption.AllDirectories)
            : [];
    }

    private static IEnumerable<string> AssembliesUnder(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.dll", SearchOption.AllDirectories)
            : [];
}
