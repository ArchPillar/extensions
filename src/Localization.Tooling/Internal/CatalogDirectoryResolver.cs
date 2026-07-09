namespace ArchPillar.Extensions.Localization.Tooling.Internal;

/// <summary>
/// Resolves a project / solution / directory scope to the catalog directories that hold a project's dev/source
/// catalogs — the mirror of <see cref="ScopeResolver"/> for the catalog-consuming commands (<c>export</c>,
/// <c>import</c>, <c>merge</c>, <c>manifest</c>), so they too work over a whole app at once instead of one
/// catalog folder at a time.
/// <para>
/// Catalogs live by convention in a <c>Translations</c> folder beside each project (the folder every doc and
/// sample authors to with <c>--output Translations</c>), so a project resolves to <c>&lt;projectdir&gt;/Translations</c>.
/// That default is overridable: pass <c>--input &lt;dir&gt;</c> to point at an explicit catalog folder (a project
/// that authored to a different <c>--output</c> folder passes <c>--input</c>).
/// </para>
/// <para>
/// <c>--assembly</c> does not apply here: there is no IL to read off a catalog, so these commands take
/// <c>--input</c> as their low-level "this exact folder" form instead of an assembly path.
/// </para>
/// </summary>
internal static class CatalogDirectoryResolver
{
    private const string CatalogFolderName = "Translations";

    /// <summary>The catalog directories to read for the gather-many commands (<c>export</c>, <c>merge</c>,
    /// <c>manifest</c>), deduplicated (OrdinalIgnoreCase) and stably ordered. Only directories that exist are
    /// returned. An explicit <c>--input</c> overrides everything; otherwise each in-scope project contributes
    /// its <c>Translations</c> folder, falling back to the current directory's own <c>Translations</c>.</summary>
    public static IReadOnlyList<string> ResolveDirectories(ScopeOptions scope)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<string> ordered = [];
        foreach (var directory in CandidateDirectories(scope))
        {
            var full = Path.GetFullPath(directory);
            if (Directory.Exists(full) && seen.Add(full))
            {
                ordered.Add(full);
            }
        }

        return ordered;
    }

    /// <summary>
    /// Each in-scope project's name mapped to its directory, so <c>import</c> routes a returned catalog back to the
    /// project that owns it (by the assembly name the entry carries) — the per-project layout the authoring
    /// commands wrote. Empty when the scope discovers no projects.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ProjectDirectoriesByName(ScopeOptions scope)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var projectFile in ScopeProjects(scope))
        {
            map[Path.GetFileNameWithoutExtension(projectFile)] = Path.GetDirectoryName(projectFile)!;
        }

        return map;
    }

    /// <summary>
    /// The base directory a scope's catalogs sit under — the project's, the solution's, or the current directory —
    /// which a relative catalog folder (e.g. <c>Translations</c>) resolves against for an assembly with no matching
    /// project.
    /// </summary>
    public static string ScopeBaseDirectory(ScopeOptions scope)
    {
        if (scope.Project is { } project)
        {
            return Path.GetDirectoryName(ScopeDiscovery.ResolveSingleFile(project, "project", "*.csproj"))!;
        }

        if (scope.Solution is { } solution)
        {
            return Path.GetDirectoryName(ScopeDiscovery.ResolveSingleFile(solution, "solution", "*.sln", "*.slnx"))!;
        }

        CurrentDirectoryScope current = ScopeDiscovery.DiscoverCurrentDirectory();
        if (current.Solution is { } discoveredSolution)
        {
            return Path.GetDirectoryName(discoveredSolution)!;
        }

        return current.Project is { } discoveredProject ? Path.GetDirectoryName(discoveredProject)! : current.Directory;
    }

    private static IEnumerable<string> ScopeProjects(ScopeOptions scope)
    {
        if (scope.Project is { } project)
        {
            return ScopeDiscovery.ProjectClosure(ScopeDiscovery.ResolveSingleFile(project, "project", "*.csproj"), scope.Recurse);
        }

        if (scope.Solution is { } solution)
        {
            return ScopeDiscovery.SolutionProjects(ScopeDiscovery.ResolveSingleFile(solution, "solution", "*.sln", "*.slnx"));
        }

        CurrentDirectoryScope current = ScopeDiscovery.DiscoverCurrentDirectory();
        if (current.Solution is { } discoveredSolution)
        {
            return ScopeDiscovery.SolutionProjects(discoveredSolution);
        }

        return current.Project is { } discoveredProject ? [discoveredProject] : [];
    }

    private static IEnumerable<string> CandidateDirectories(ScopeOptions scope)
    {
        if (scope.Input is { Length: > 0 } input)
        {
            return [Path.GetFullPath(input)];
        }

        return scope switch
        {
            { Project: { } project } => ScopeDiscovery
                .ProjectClosure(ScopeDiscovery.ResolveSingleFile(project, "project", "*.csproj"), scope.Recurse)
                .Select(ProjectCatalogDirectory),
            { Solution: { } solution } => ScopeDiscovery
                .SolutionProjects(ScopeDiscovery.ResolveSingleFile(solution, "solution", "*.sln", "*.slnx"))
                .Select(ProjectCatalogDirectory),
            _ => DiscoverInCurrentDirectory()
        };
    }

    // With no scope, mirror ScopeResolver's current-directory default: a lone solution -> its projects'
    // Translations; else a lone project -> its Translations; else the current directory's own Translations if
    // it exists; else a helpful error in the same style ScopeResolver uses.
    private static IEnumerable<string> DiscoverInCurrentDirectory()
    {
        CurrentDirectoryScope current = ScopeDiscovery.DiscoverCurrentDirectory();
        if (current.Solution is { } solution)
        {
            return ScopeDiscovery.SolutionProjects(solution).Select(ProjectCatalogDirectory);
        }

        if (current.Project is { } project)
        {
            return [ProjectCatalogDirectory(project)];
        }

        var cwdCatalogs = Path.Combine(current.Directory, CatalogFolderName);
        if (Directory.Exists(cwdCatalogs))
        {
            return [cwdCatalogs];
        }

        throw new ArgumentException("No project or solution found in the current directory. Run from your app folder, or pass --project, --solution, or --input <dir>.");
    }

    /// <summary>
    /// The catalog directory the authoring commands (<c>extract</c>/<c>add</c>/<c>sync</c>) write an assembly's
    /// catalogs into: the <paramref name="folder"/> subfolder of the project that built it (found by walking up to
    /// the nearest <c>.csproj</c>) so a whole app's catalogs stay beside their own projects; or, for a loose
    /// <c>--assembly</c>/<c>--input</c> path with no project, the same subfolder beside the input base. Never one
    /// shared flat folder across separate projects. (An absolute <paramref name="folder"/> from an explicit
    /// <c>--output</c> wins, the low-level escape.)
    /// </summary>
    public static string CatalogDirectoryFor(string assemblyPath, ScopeOptions scope, string folder) =>
        ProjectCatalogDirectoryOf(assemblyPath, folder) ?? Path.Combine(InputBase(scope, assemblyPath), folder);

    /// <summary>The <paramref name="folder"/> subfolder of the assembly's project, or null when it is not in a
    /// project tree.</summary>
    public static string? ProjectCatalogDirectoryOf(string assemblyPath, string folder)
    {
        for (var directory = Path.GetDirectoryName(Path.GetFullPath(assemblyPath)); directory is not null; directory = Path.GetDirectoryName(directory))
        {
            if (Directory.EnumerateFiles(directory, "*.csproj").Any())
            {
                return Path.Combine(directory, folder);
            }
        }

        return null;
    }

    // The base a loose assembly's catalogs sit beside: the --input directory, the --assembly's own directory, or
    // (failing both) the assembly file's directory.
    private static string InputBase(ScopeOptions scope, string assemblyPath)
    {
        if (scope.Input is { Length: > 0 } input)
        {
            return Path.GetFullPath(input);
        }

        if (scope.Assembly is { Length: > 0 } assembly)
        {
            return Path.GetDirectoryName(Path.GetFullPath(assembly))!;
        }

        return Path.GetDirectoryName(Path.GetFullPath(assemblyPath))!;
    }

    private static string ProjectCatalogDirectory(string projectPath) =>
        Path.Combine(Path.GetDirectoryName(projectPath)!, CatalogFolderName);
}
