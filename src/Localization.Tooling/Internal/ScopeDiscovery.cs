using System.Xml.Linq;

namespace ArchPillar.Extensions.Localization.Tooling.Internal;

/// <summary>
/// Shared project / solution discovery used by both <see cref="ScopeResolver"/> (which resolves a scope to
/// built assemblies) and <see cref="CatalogDirectoryResolver"/> (which resolves the same scope to catalog
/// directories). The two resolvers differ only in what they read off a project — its bin tree versus its
/// <c>Translations</c> folder — so the logic that finds the project / solution files, walks transitive
/// project references, and applies the current-directory default lives here once.
/// </summary>
internal static class ScopeDiscovery
{
    // Resolves a --project / --solution value to a single file. A file path is used as-is; an empty value
    // means the current directory, a directory value means that directory — in which case the lone matching
    // file is taken (matching how `dotnet build` finds the project/solution when run in a folder), and an
    // ambiguous or empty directory is an error rather than a silent guess.
    public static string ResolveSingleFile(string value, string kind, params string[] patterns)
    {
        if (value.Length > 0 && File.Exists(value))
        {
            return Path.GetFullPath(value);
        }

        var directory = value.Length == 0 ? Directory.GetCurrentDirectory() : Path.GetFullPath(value);
        if (!Directory.Exists(directory))
        {
            throw new ArgumentException($"'{value}' is not a {kind} file or a directory.");
        }

        List<string> matches = [.. patterns.SelectMany(pattern => Directory.EnumerateFiles(directory, pattern)).Distinct(StringComparer.OrdinalIgnoreCase)];
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new ArgumentException($"No {kind} file found in '{directory}'."),
            _ => throw new ArgumentException($"Multiple {kind} files in '{directory}'; pass one explicitly with --{kind} <path>.")
        };
    }

    /// <summary>The project file itself plus, when <paramref name="recurse"/> is set, every project it
    /// references transitively — so a scope rooted at an app also covers the libraries it pulls in.</summary>
    public static IEnumerable<string> ProjectClosure(string projectPath, bool recurse)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        queue.Enqueue(projectPath);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!seen.Add(current) || !File.Exists(current))
            {
                continue;
            }

            yield return current;
            if (!recurse)
            {
                continue;
            }

            foreach (var referenced in ProjectReferences(current))
            {
                queue.Enqueue(referenced);
            }
        }
    }

    /// <summary>The project files a solution lists (classic <c>.sln</c> or XML <c>.slnx</c>), as absolute
    /// paths.</summary>
    public static IEnumerable<string> SolutionProjects(string solutionPath)
    {
        var directory = Path.GetDirectoryName(solutionPath)!;
        if (Path.GetExtension(solutionPath).Equals(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            return XDocument.Load(solutionPath)
                .Descendants()
                .Where(e => e.Name.LocalName == "Project")
                .Select(e => e.Attribute("Path")?.Value)
                .Where(path => path is { Length: > 0 } && path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                .Select(path => Path.GetFullPath(Path.Combine(directory, path!.Replace('\\', Path.DirectorySeparatorChar))));
        }

        // Classic .sln: lines like  Project("{GUID}") = "Name", "relative\path.csproj", "{GUID}"
        return File.ReadLines(solutionPath)
            .Where(line => line.StartsWith("Project(", StringComparison.Ordinal))
            .Select(ProjectPathFromSlnLine)
            .Where(path => path is { Length: > 0 })
            .Select(path => Path.GetFullPath(Path.Combine(directory, path!.Replace('\\', Path.DirectorySeparatorChar))))!;
    }

    /// <summary>The lone solution or project file in the current directory, or an
    /// <see cref="ArgumentException"/> when the directory is empty or ambiguous — the shared current-directory
    /// default both resolvers use when no scope option is given.</summary>
    public static CurrentDirectoryScope DiscoverCurrentDirectory()
    {
        var directory = Directory.GetCurrentDirectory();
        List<string> solutions = [.. Directory.EnumerateFiles(directory, "*.sln").Concat(Directory.EnumerateFiles(directory, "*.slnx"))];
        if (solutions.Count > 1)
        {
            throw new ArgumentException("Multiple solution files in the current directory; pass one with --solution <path>.");
        }

        if (solutions.Count == 1)
        {
            return new CurrentDirectoryScope(solutions[0], null, directory);
        }

        List<string> projects = [.. Directory.EnumerateFiles(directory, "*.csproj")];
        return projects.Count switch
        {
            1 => new CurrentDirectoryScope(null, projects[0], directory),
            0 => new CurrentDirectoryScope(null, null, directory),
            _ => throw new ArgumentException("Multiple project files in the current directory; pass one with --project <path>.")
        };
    }

    private static IEnumerable<string> ProjectReferences(string projectPath)
    {
        var directory = Path.GetDirectoryName(projectPath)!;
        foreach (XElement element in XDocument.Load(projectPath).Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
        {
            var include = element.Attribute("Include")?.Value;
            if (!string.IsNullOrEmpty(include))
            {
                yield return Path.GetFullPath(Path.Combine(directory, include!.Replace('\\', Path.DirectorySeparatorChar)));
            }
        }
    }

    private static string? ProjectPathFromSlnLine(string line)
    {
        var parts = line.Split(',');
        if (parts.Length < 2)
        {
            return null;
        }

        var quoted = parts[1].Trim().Trim('"');
        return quoted.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ? quoted : null;
    }
}

/// <summary>
/// What the current directory resolved to when no scope option was given: either a lone solution, a lone
/// project, or neither (both null). <see cref="Directory"/> is the current directory itself, used as a
/// final fallback (e.g. its <c>Translations</c> folder) when no project or solution is present.
/// </summary>
internal sealed record CurrentDirectoryScope(string? Solution, string? Project, string Directory);
