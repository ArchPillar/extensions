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

    /// <summary>The single directory <c>import</c> writes returned translations into when <c>--output</c> is
    /// omitted: a project's / solution's <c>Translations</c> folder, or the current directory's. The directory
    /// need not pre-exist (the caller creates it); <c>--input</c> is the zip here, not the write target, so it
    /// is ignored.</summary>
    public static string ResolveWriteDirectory(ScopeOptions scope)
    {
        if (scope.Project is { } project)
        {
            return ProjectCatalogDirectory(ScopeDiscovery.ResolveSingleFile(project, "project", "*.csproj"));
        }

        if (scope.Solution is { } solution)
        {
            return Path.Combine(Path.GetDirectoryName(ScopeDiscovery.ResolveSingleFile(solution, "solution", "*.sln", "*.slnx"))!, CatalogFolderName);
        }

        return Path.Combine(Directory.GetCurrentDirectory(), CatalogFolderName);
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

    private static string ProjectCatalogDirectory(string projectPath) =>
        Path.Combine(Path.GetDirectoryName(projectPath)!, CatalogFolderName);
}
